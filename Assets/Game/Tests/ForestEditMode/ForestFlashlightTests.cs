using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Gamex.EditorTools;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Gamex.Tests.Forest
{
    /// <summary>
    /// The player's torch (N1, build/issues/N1-night.md): the component on its own (switching, the hand-held lag, the beam pattern
    /// and its photometry), the torch on the forest's player, and the Night preset as the scene's default.
    /// </summary>
    [Category("Forest")]
    public class ForestFlashlightTests
    {
        // ---- The component on its own ----

        [Test]
        public void Flashlight_StartsOnAndToggles()
        {
            var holder = new GameObject("Flashlight Test") { hideFlags = HideFlags.HideAndDontSave };
            Texture cookie = null;
            try
            {
                var light = holder.AddComponent<Light>();
                var torch = holder.AddComponent<PlayerFlashlight>();
                Assert.IsTrue(torch.IsOn, "A new torch must start switched on.");
                Assert.IsTrue(light.enabled, "A torch that is on must have its light enabled.");
                Assert.AreEqual(LightType.Spot, light.type, "The torch is a spot light.");
                cookie = light.cookie;
                Assert.IsNotNull(cookie, "A torch without a cookie of its own must generate its beam pattern.");

                var events = new List<bool>();
                torch.IsOnChanged += events.Add;

                torch.Toggle();
                Assert.IsFalse(torch.IsOn);
                Assert.IsFalse(light.enabled, "A torch that is off must have its light disabled.");
                CollectionAssert.AreEqual(new[] { false }, events, "Switching off must raise IsOnChanged(false) once.");

                torch.SetOn(false);
                Assert.AreEqual(1, events.Count, "Setting the state it already has must not raise the event.");

                torch.SetOn(true);
                Assert.IsTrue(torch.IsOn);
                Assert.IsTrue(light.enabled);
                CollectionAssert.AreEqual(new[] { false, true }, events);

                torch.Toggle();
                torch.Toggle();
                CollectionAssert.AreEqual(new[] { false, true, false, true }, events);
            }
            finally
            {
                Object.DestroyImmediate(holder);
                if (cookie != null)
                    Object.DestroyImmediate(cookie);
            }
        }

        [Test]
        public void Flashlight_PhotometryAndBeamPattern()
        {
            // The integral of cone falloff times pattern can never exceed the cone's solid angle, and the pattern's hot spot alone
            // (radius 0.22 of the cone) carries a few per cent of it.
            const float outer = 60f, inner = 40f;
            float coneSolidAngle = 2f * Mathf.PI * (1f - Mathf.Cos(outer * 0.5f * Mathf.Deg2Rad));
            float lumensPerCandela = PlayerFlashlight.LumensPerCandela(outer, inner);
            Assert.Less(lumensPerCandela, coneSolidAngle, "More light than a uniform cone of the peak intensity could carry.");
            Assert.Greater(lumensPerCandela, 0.03f * coneSolidAngle, "Too little light for a beam with a hot spot and a spill.");
            Assert.AreEqual(300f, PlayerFlashlight.CandelaForLumens(300f, outer, inner) * lumensPerCandela, 0.01f,
                "CandelaForLumens must invert LumensPerCandela.");
            Assert.Greater(PlayerFlashlight.LumensPerCandela(70f, inner), lumensPerCandela, "A wider outer cone must carry more light per candela.");

            // The pattern: a bright centre, a hot spot that falls off, a dim spill, nothing outside the cone.
            Assert.Greater(PlayerFlashlight.BeamPattern(0.05f, 0f), 0.8f, "The hot spot must be near full brightness.");
            float spill = PlayerFlashlight.BeamPattern(0.6f, 0f);
            Assert.That(spill, Is.InRange(0.05f, 0.3f), "The spill must be a dim fraction of the hot spot.");
            Assert.Greater(PlayerFlashlight.BeamPattern(0.15f, 0f), spill);
            Assert.AreEqual(0f, PlayerFlashlight.BeamPattern(1f, 0f), "Nothing outside the outer cone.");
            Assert.AreEqual(0f, PlayerFlashlight.BeamPattern(1.3f, 1f), "Nothing outside the outer cone.");
            for (float radius = 0f; radius < 1.2f; radius += 0.01f)
                for (float angle = -3.1f; angle < 3.2f; angle += 0.5f)
                    Assert.That(PlayerFlashlight.BeamPattern(radius, angle), Is.InRange(0f, 1f), $"Pattern out of range at {radius}, {angle}.");

            Texture2D cookie = PlayerFlashlight.CreateCookie(64);
            try
            {
                Assert.AreEqual(64, cookie.width);
                Assert.AreEqual(TextureWrapMode.Clamp, cookie.wrapMode, "A cookie must not repeat.");
                Assert.Greater(cookie.mipmapCount, 1, "The cookie needs mipmaps, or distant pattern edges alias.");
                Color centre = cookie.GetPixel(32, 32), corner = cookie.GetPixel(0, 0), edge = cookie.GetPixel(63, 32);
                Assert.Greater(centre.r, 0.8f, "The cookie's centre is the hot spot.");
                Assert.AreEqual(0f, corner.r, 1e-3f, "The cookie's corners lie outside the cone.");
                Assert.Less(edge.r, 0.05f, "The cookie's edge is the cone's edge.");
                Assert.AreEqual(centre.r, centre.g, 1e-3f, "The pattern is grey: the light's colour is its temperature.");
            }
            finally
            {
                Object.DestroyImmediate(cookie);
            }
        }

        [Test]
        public void Flashlight_LagIsSmoothAndBounded()
        {
            const float timeConstant = 0.07f, maxLag = 10f;
            Quaternion target = Quaternion.Euler(0f, 40f, 0f);

            // A sudden turn of 40 degrees: after one frame the beam is behind, but never further than the limit.
            Quaternion next = PlayerFlashlight.SmoothTowards(Quaternion.identity, target, 1f / 60f, timeConstant, maxLag);
            Assert.LessOrEqual(Quaternion.Angle(next, target), maxLag + 1e-3f, "The beam trails the view by more than the limit.");
            Assert.Greater(Quaternion.Angle(next, target), 1f, "The beam does not lag at all.");

            // It catches up: after half a second it is within a tenth of a degree.
            Quaternion current = next;
            for (int i = 0; i < 30; i++)
                current = PlayerFlashlight.SmoothTowards(current, target, 1f / 60f, timeConstant, maxLag);
            Assert.Less(Quaternion.Angle(current, target), 0.1f, "The beam does not catch up with the view.");

            // Independent of the frame rate: 60 steps of 1/60 s and 120 steps of 1/120 s from inside the limit end in the same place.
            Quaternion start = Quaternion.Euler(0f, 32f, 0f), at60 = start, at120 = start;
            for (int i = 0; i < 6; i++)
                at60 = PlayerFlashlight.SmoothTowards(at60, target, 1f / 60f, timeConstant, maxLag);
            for (int i = 0; i < 12; i++)
                at120 = PlayerFlashlight.SmoothTowards(at120, target, 1f / 120f, timeConstant, maxLag);
            Assert.Less(Quaternion.Angle(at60, at120), 0.05f, "The lag depends on the frame rate.");

            Assert.AreEqual(0f, Quaternion.Angle(target, PlayerFlashlight.SmoothTowards(start, target, 1f / 60f, 0f, maxLag)), 1e-3f,
                "A time constant of zero means no lag.");
        }

        // ---- The torch on the forest's player ----

        [Test]
        public void Flashlight_IsOnTheForestPlayer()
        {
            Scene scene = ForestTestSupport.RequireForestScene();
            GameObject player = ForestTestSupport.RequireRoot(scene, "Player");
            PlayerFlashlight[] torches = player.GetComponentsInChildren<PlayerFlashlight>(true);
            Assert.AreEqual(1, torches.Length, "The forest's player must carry exactly one torch.");
            PlayerFlashlight torch = torches[0];
            Transform pivot = player.transform.Find("CameraPivot");
            Assert.AreSame(pivot, torch.transform.parent, "The torch must hang on the camera pivot, so it turns with the view.");
            Assert.AreEqual(ForestFlashlight.ObjectName, torch.name);
            Assert.IsTrue(torch.gameObject.activeSelf);

            var serialized = new SerializedObject(torch);
            Assert.IsTrue(serialized.FindProperty("startOn").boolValue, "The torch must start switched on.");

            // The scene's own component is read through its Light: its properties would initialise it in edit mode.
            var light = torch.GetComponent<Light>();
            Assert.AreEqual(LightType.Spot, light.type);
            Assert.IsTrue(light.enabled, "The saved light must be on, as the torch starts.");
            Assert.AreNotEqual(LightShadows.None, light.shadows, "The torch must cast shadows.");
            Assert.AreEqual(LightUnit.Candela, light.lightUnit);
            Assert.AreEqual(torch.PeakCandela, light.intensity, torch.PeakCandela * 1e-3f, "The light's intensity must be the torch's lumens in candela.");
            Assert.That(torch.Lumens, Is.InRange(100f, 1000f), "A hand torch gives some hundred lumens.");
            Assert.That(torch.PeakCandela, Is.InRange(500f, 10000f), "A hand torch's hot spot is some thousand candela.");
            Assert.Less(light.innerSpotAngle, light.spotAngle, "The beam needs an inner and an outer cone.");
            Assert.IsNotNull(light.cookie, "The torch needs its beam pattern.");
            Assert.AreEqual(ForestFlashlight.CookiePath, AssetDatabase.GetAssetPath(light.cookie), "The beam pattern must be the generated asset.");

            var data = torch.GetComponent<HDAdditionalLightData>();
            Assert.IsNotNull(data, "The torch's light has no HDRP data.");
            Assert.AreEqual(ShadowUpdateMode.EveryFrame, data.shadowUpdateMode, "The torch moves with the view: its shadows must be drawn every frame.");
            Assert.IsTrue(data.shadowResolution.useOverride, "The torch's shadow resolution must be set, not left to the tier.");
            Assert.AreEqual(ForestFlashlight.ShadowResolution, data.shadowResolution.@override);
            Assert.IsTrue(data.affectsVolumetric, "The beam must show in the mist.");
            Assert.Greater(data.volumetricDimmer, 0f, "The beam must show in the mist.");

            // The greybox does not change in this step.
            if (File.Exists(GreyboxSceneBuilder.ScenePath))
            {
                Scene greybox = EditorSceneManager.OpenScene(GreyboxSceneBuilder.ScenePath, OpenSceneMode.Single);
                GameObject greyboxPlayer = ForestTestSupport.Root(greybox, "Player");
                Assert.IsNotNull(greyboxPlayer, "The greybox scene has no Player.");
                Assert.AreEqual(0, greyboxPlayer.GetComponentsInChildren<PlayerFlashlight>(true).Length, "The greybox's player has a torch; the greybox must not change in N1.");
            }
        }

        // ---- The night as the default ----

        [Test]
        public void Lighting_NightIsTheDefaultAndOvercastStaysBuildable()
        {
            Assert.AreEqual("Night", ForestLightingPresets.Default, "Night is the game's main mood (N1).");
            CollectionAssert.Contains(ForestLightingPresets.Names, "Night");
            CollectionAssert.Contains(ForestLightingPresets.Names, "OvercastMist", "OvercastMist must stay buildable.");
            Assert.AreEqual("OvercastMist", ForestLightingPresets.Inspect, "Kit turntables and the layout's backlit view must stay on the daylight preset.");

            ForestLightingPresets.Preset night = ForestLightingPresets.Get("Night");
            Assert.IsNotNull(night);
            Assert.AreEqual("qwantani_night_puresky", night.skyId);
            Assert.Less(night.keyLux, 1f, "Moonlight is a fraction of a lux.");
            Assert.Less(night.skyLux, night.keyLux, "The moonless sky gives less light than the moon.");

            // Both presets are reachable from the menu.
            foreach (string method in new[] { nameof(ForestLighting.ApplyNight), nameof(ForestLighting.ApplyOvercastMist) })
            {
                MethodInfo info = typeof(ForestLighting).GetMethod(method, BindingFlags.Public | BindingFlags.Static);
                Assert.IsNotNull(info, $"ForestLighting.{method} is missing.");
                Assert.IsNotNull(info.GetCustomAttribute<MenuItem>(), $"ForestLighting.{method} has no menu item.");
            }

            // The built scene holds both, and the night is what it starts in unless it was built otherwise on purpose.
            Scene scene = ForestTestSupport.RequireForestScene();
            Transform lighting = ForestTestSupport.RequireRoot(scene, "Lighting").transform;
            Assert.IsNotNull(lighting.Find("Preset_Night"), "The scene has no Night preset; is qwantani_night_puresky downloaded?");
            Assert.IsNotNull(lighting.Find("Preset_OvercastMist"), "The scene has no OvercastMist preset.");
            Assert.AreEqual("Night", ForestLighting.ActivePreset(), "The scene was not built at night, its default.");
        }
    }
}
