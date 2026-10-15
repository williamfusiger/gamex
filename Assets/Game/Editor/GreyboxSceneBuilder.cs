using System.Collections.Generic;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.SceneManagement;

namespace Gamex.EditorTools
{
    /// <summary>
    /// Generates the greybox test level from code: a patch of forest with the player at the south
    /// edge, the rescue radio at the north edge and one raptor patrolling in between. Keeping the
    /// layout here means it can be changed and rebuilt without hand-editing the scene file.
    /// </summary>
    public static class GreyboxSceneBuilder
    {
        public const string ScenePath = "Assets/Game/Scenes/Greybox.unity";

        const string ScenesFolder = "Assets/Game/Scenes";
        const string AssetFolderName = "Greybox";
        const string AssetFolder = ScenesFolder + "/" + AssetFolderName;

        const float HalfSize = 60f;
        const int Seed = 1993;
        const int TreeCount = 240;
        const int BoulderCount = 28;
        const int ScatteredRockCount = 16;

        static readonly Vector3 PlayerSpawn = new(0f, 0f, -52f);
        static readonly Vector3 RadioPosition = new(0f, 0f, 50f);

        static readonly Vector3[] PatrolRoute =
        {
            new(-30f, 0f, -8f), new(-8f, 0f, 14f), new(24f, 0f, 22f), new(36f, 0f, -6f), new(8f, 0f, -22f),
        };

        static readonly Vector3[] HidingSpotPositions =
        {
            new(-6f, 0f, -40f), new(9f, 0f, -30f), new(-14f, 0f, -22f), new(2f, 0f, -12f),
            new(18f, 0f, -8f), new(-20f, 0f, 2f), new(-2f, 0f, 8f), new(14f, 0f, 10f),
            new(-10f, 0f, 24f), new(6f, 0f, 30f), new(22f, 0f, 34f), new(-8f, 0f, 42f),
        };

        static readonly Vector3[] StartingRocks =
        {
            new(-1.5f, 0f, -49f), new(1.2f, 0f, -48.5f), new(0.3f, 0f, -47f), new(-0.8f, 0f, -46f),
        };

        // Circles on the ground that are already taken, so scattered props never overlap or block
        // the places the level depends on.
        static readonly List<(Vector3 centre, float radius)> occupied = new();
        static System.Random random;

        [MenuItem("Gamex/Rebuild Greybox Scene")]
        public static void Build()
        {
            if (!Application.isBatchMode)
            {
                bool confirmed = EditorUtility.DisplayDialog("Rebuild Greybox Scene",
                    "This regenerates the Greybox scene from code. Anything edited by hand in that scene is lost.",
                    "Rebuild", "Cancel");
                if (!confirmed || !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                    return;
            }

            random = new System.Random(Seed);
            occupied.Clear();

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            AssetDatabase.DeleteAsset(AssetFolder);
            AssetDatabase.CreateFolder(ScenesFolder, AssetFolderName);
            AssetDatabase.CreateFolder(AssetFolder, "Materials");

            Reserve(PlayerSpawn, 7f);
            Reserve(RadioPosition, 8f);
            foreach (Vector3 point in PatrolRoute)
                Reserve(point, 3.5f);
            foreach (Vector3 point in HidingSpotPositions)
                Reserve(point, 4f);

            BuildLighting();

            // Everything under this object is static level geometry and is what the navmesh is baked from.
            var environment = new GameObject("Environment");
            BuildGround(environment.transform);
            HidingSpot[] hidingSpots = BuildHidingSpots(environment.transform);
            GameObject radio = BuildRadio(environment.transform);
            BuildTrees(environment.transform);
            BuildBoulders(environment.transform);
            BakeNavMesh(environment);

            PlayerController player = BuildPlayer();
            GameSession session = BuildSession(player);
            RaptorAI raptor = BuildRaptor(session);
            BuildThrowables();

            WireRef(radio.GetComponent<RescueRadio>(), "session", session);
            WireArray(session.GetComponent<PrototypeHud>(), "raptors", new Object[] { raptor });

            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            AssetDatabase.SaveAssets();

            Debug.Log($"Greybox scene rebuilt: {hidingSpots.Length} hiding spots, {occupied.Count} reserved areas.");
        }

        static void BuildLighting()
        {
            var moon = new GameObject("Moon");
            moon.AddComponent<HDAdditionalLightData>();
            var light = moon.GetComponent<Light>();
            light.type = LightType.Directional;
            light.color = new Color(0.62f, 0.72f, 1f);
            light.lightUnit = LightUnit.Lux;
            light.intensity = 6f;
            light.shadows = LightShadows.Soft;
            moon.transform.rotation = Quaternion.Euler(48f, -35f, 0f);

            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, $"{AssetFolder}/Atmosphere.asset");

            AddOverride<VisualEnvironment>(profile).skyType.Override((int)SkyType.Gradient);

            var sky = AddOverride<GradientSky>(profile);
            sky.top.Override(new Color(0.01f, 0.02f, 0.05f));
            sky.middle.Override(new Color(0.05f, 0.08f, 0.13f));
            sky.bottom.Override(new Color(0.03f, 0.04f, 0.05f));

            var fog = AddOverride<Fog>(profile);
            fog.enabled.Override(true);
            fog.meanFreePath.Override(45f);
            fog.baseHeight.Override(0f);
            fog.maximumHeight.Override(25f);
            fog.enableVolumetricFog.Override(true);
            fog.albedo.Override(new Color(0.75f, 0.82f, 0.9f));

            // Fixed, so the night stays dark instead of the camera brightening it to daylight.
            var exposure = AddOverride<Exposure>(profile);
            exposure.mode.Override(ExposureMode.Fixed);
            exposure.fixedExposure.Override(-1f);

            EditorUtility.SetDirty(profile);

            var volume = new GameObject("Atmosphere").AddComponent<Volume>();
            volume.isGlobal = true;
            volume.sharedProfile = profile;
        }

        static T AddOverride<T>(VolumeProfile profile) where T : VolumeComponent
        {
            T component = profile.Add<T>();
            component.hideFlags = HideFlags.HideInInspector | HideFlags.HideInHierarchy;
            AssetDatabase.AddObjectToAsset(component, profile);
            return component;
        }

        static void BuildGround(Transform parent)
        {
            Material groundMaterial = CreateMaterial("Ground", new Color(0.16f, 0.2f, 0.12f));
            Material cliffMaterial = CreateMaterial("Cliff", new Color(0.12f, 0.12f, 0.13f));

            float size = HalfSize * 2f;
            Box("Ground", parent, groundMaterial, new Vector3(0f, -0.5f, 0f), new Vector3(size, 1f, size));

            var bounds = new GameObject("Bounds").transform;
            bounds.SetParent(parent);
            Box("North", bounds, cliffMaterial, new Vector3(0f, 4f, HalfSize + 1f), new Vector3(size + 4f, 8f, 2f));
            Box("South", bounds, cliffMaterial, new Vector3(0f, 4f, -HalfSize - 1f), new Vector3(size + 4f, 8f, 2f));
            Box("East", bounds, cliffMaterial, new Vector3(HalfSize + 1f, 4f, 0f), new Vector3(2f, 8f, size));
            Box("West", bounds, cliffMaterial, new Vector3(-HalfSize - 1f, 4f, 0f), new Vector3(2f, 8f, size));
        }

        static HidingSpot[] BuildHidingSpots(Transform parent)
        {
            Material material = CreateMaterial("HidingSpot", new Color(0.1f, 0.45f, 0.42f));
            var group = new GameObject("HidingSpots").transform;
            group.SetParent(parent);

            var spots = new HidingSpot[HidingSpotPositions.Length];
            for (int i = 0; i < spots.Length; i++)
            {
                var root = new GameObject($"HidingSpot {i + 1}");
                root.transform.SetParent(group);
                root.transform.SetPositionAndRotation(HidingSpotPositions[i], Quaternion.Euler(0f, Range(0f, 360f), 0f));

                // The box is solid: it hides the player from sight rays and is carved out of the navmesh.
                GameObject visual = Box("Visual", root.transform, material, Vector3.zero, new Vector3(1.6f, 1.3f, 1.6f));
                visual.transform.localPosition = new Vector3(0f, 0.65f, 0f);
                visual.transform.localRotation = Quaternion.identity;

                Transform hidePoint = Child("HidePoint", root.transform, Vector3.zero);
                Transform exitPoint = Child("ExitPoint", root.transform, new Vector3(0f, 0.05f, 1.8f));

                spots[i] = root.AddComponent<HidingSpot>();
                WireRef(spots[i], "hidePoint", hidePoint);
                WireRef(spots[i], "exitPoint", exitPoint);
            }
            return spots;
        }

        static GameObject BuildRadio(Transform parent)
        {
            Material material = CreateMaterial("Radio", new Color(0.85f, 0.65f, 0.1f));

            var root = new GameObject("RescueRadio");
            root.transform.SetParent(parent);
            root.transform.position = RadioPosition;
            root.AddComponent<RescueRadio>();

            Box("Cabinet", root.transform, material, RadioPosition + new Vector3(0f, 0.8f, 0f), new Vector3(1f, 1.6f, 1f));
            GameObject mast = Box("Mast", root.transform, material, RadioPosition + new Vector3(0f, 4.5f, 0f), new Vector3(0.12f, 6f, 0.12f));
            Object.DestroyImmediate(mast.GetComponent<Collider>());

            // A beacon the player can pick out through the fog from a distance.
            var beacon = new GameObject("Beacon");
            beacon.transform.SetParent(root.transform);
            beacon.transform.position = RadioPosition + new Vector3(0f, 3f, 1f);
            beacon.AddComponent<HDAdditionalLightData>();
            var light = beacon.GetComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(1f, 0.75f, 0.4f);
            light.lightUnit = LightUnit.Lumen;
            light.intensity = 250f;
            light.range = 30f;

            return root;
        }

        static void BuildTrees(Transform parent)
        {
            Material trunkMaterial = CreateMaterial("Trunk", new Color(0.2f, 0.15f, 0.1f));
            Material canopyMaterial = CreateMaterial("Canopy", new Color(0.07f, 0.16f, 0.08f));
            var group = new GameObject("Trees").transform;
            group.SetParent(parent);

            for (int i = 0; i < TreeCount; i++)
            {
                float diameter = Range(0.7f, 1.6f);
                if (!TryFindFreeSpot(diameter * 0.5f + 0.8f, out Vector3 position))
                    break;

                float height = Range(7f, 11f);
                var trunk = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                trunk.name = "Tree";
                trunk.transform.SetParent(group);
                trunk.transform.position = position + Vector3.up * (height * 0.5f);
                trunk.transform.localScale = new Vector3(diameter, height * 0.5f, diameter);
                trunk.GetComponent<Renderer>().sharedMaterial = trunkMaterial;

                // Canopies only shade the ground; they have no collider.
                var canopy = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                canopy.name = "Canopy";
                Object.DestroyImmediate(canopy.GetComponent<Collider>());
                canopy.transform.position = position + Vector3.up * height;
                canopy.transform.localScale = new Vector3(Range(4f, 6f), Range(2.5f, 3.5f), Range(4f, 6f));
                canopy.transform.SetParent(trunk.transform, true);
                canopy.GetComponent<Renderer>().sharedMaterial = canopyMaterial;
            }
        }

        static void BuildBoulders(Transform parent)
        {
            Material material = CreateMaterial("Boulder", new Color(0.25f, 0.25f, 0.26f));
            var group = new GameObject("Boulders").transform;
            group.SetParent(parent);

            for (int i = 0; i < BoulderCount; i++)
            {
                float width = Range(2f, 4.5f);
                if (!TryFindFreeSpot(width * 0.75f + 0.8f, out Vector3 position))
                    break;

                float height = Range(1.6f, 3f);
                GameObject boulder = Box("Boulder", group, material, position + Vector3.up * (height * 0.4f),
                    new Vector3(width, height, Range(2f, 4.5f)));
                boulder.transform.rotation = Quaternion.Euler(Range(-8f, 8f), Range(0f, 360f), Range(-8f, 8f));
            }
        }

        static void BakeNavMesh(GameObject environment)
        {
            var surface = environment.AddComponent<NavMeshSurface>();
            surface.collectObjects = CollectObjects.Children;
            surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
            Physics.SyncTransforms();
            surface.BuildNavMesh();
            AssetDatabase.CreateAsset(surface.navMeshData, $"{AssetFolder}/NavMesh.asset");
        }

        static PlayerController BuildPlayer()
        {
            var root = new GameObject("Player");
            root.transform.position = PlayerSpawn + Vector3.up * 0.05f;

            var body = root.AddComponent<CharacterController>();
            body.height = 1.8f;
            body.radius = 0.35f;
            body.center = new Vector3(0f, 0.9f, 0f);

            Transform pivot = Child("CameraPivot", root.transform, new Vector3(0f, 1.65f, 0f));
            pivot.gameObject.tag = "MainCamera";
            var camera = pivot.gameObject.AddComponent<Camera>();
            // Short near plane so the inside of a hiding spot does not clip into view.
            camera.nearClipPlane = 0.05f;
            camera.farClipPlane = 400f;
            camera.fieldOfView = 70f;
            pivot.gameObject.AddComponent<HDAdditionalCameraData>();
            pivot.gameObject.AddComponent<AudioListener>();

            Transform holdPoint = Child("HoldPoint", pivot, new Vector3(0.35f, -0.3f, 0.7f));

            root.AddComponent<PlayerInputReader>();
            var controller = root.AddComponent<PlayerController>();
            var interactor = root.AddComponent<PlayerInteractor>();
            WireRef(controller, "cameraPivot", pivot);
            WireRef(interactor, "holdPoint", holdPoint);
            return controller;
        }

        static GameSession BuildSession(PlayerController player)
        {
            var root = new GameObject("Game");
            var session = root.AddComponent<GameSession>();
            var hud = root.AddComponent<PrototypeHud>();
            WireRef(session, "player", player);
            WireRef(hud, "session", session);
            WireRef(hud, "interactor", player.GetComponent<PlayerInteractor>());
            return session;
        }

        static RaptorAI BuildRaptor(GameSession session)
        {
            Material material = CreateMaterial("Raptor", new Color(0.55f, 0.12f, 0.08f));

            var root = new GameObject("Raptor");
            root.transform.position = PatrolRoute[0];

            var shape = root.AddComponent<CapsuleCollider>();
            shape.center = new Vector3(0f, 0.9f, 0f);
            shape.radius = 0.5f;
            shape.height = 1.8f;
            root.AddComponent<Rigidbody>().isKinematic = true;

            var agent = root.AddComponent<NavMeshAgent>();
            agent.radius = 0.5f;
            agent.height = 1.8f;
            agent.acceleration = 30f;
            agent.angularSpeed = 480f;
            agent.stoppingDistance = 0.4f;

            // Placeholder body: only there to show where the raptor is and which way it faces.
            var torso = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            Decorate(torso, "Torso", root.transform, material, new Vector3(0f, 1.05f, 0f), new Vector3(0.75f, 1f, 0.75f));
            torso.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            Decorate(GameObject.CreatePrimitive(PrimitiveType.Cube), "Head", root.transform, material,
                new Vector3(0f, 1.45f, 1.15f), new Vector3(0.32f, 0.36f, 0.8f));
            Decorate(GameObject.CreatePrimitive(PrimitiveType.Cube), "Tail", root.transform, material,
                new Vector3(0f, 1.15f, -1.6f), new Vector3(0.16f, 0.16f, 1.4f));
            Decorate(GameObject.CreatePrimitive(PrimitiveType.Cube), "LegLeft", root.transform, material,
                new Vector3(-0.25f, 0.45f, 0f), new Vector3(0.16f, 0.9f, 0.16f));
            Decorate(GameObject.CreatePrimitive(PrimitiveType.Cube), "LegRight", root.transform, material,
                new Vector3(0.25f, 0.45f, 0f), new Vector3(0.16f, 0.9f, 0.16f));

            // Inside the capsule collider on purpose: rays that start inside a collider ignore it.
            Transform eyes = Child("Eyes", root.transform, new Vector3(0f, 1.4f, 0.25f));

            var route = new GameObject("Raptor Patrol Route").transform;
            var points = new Object[PatrolRoute.Length];
            for (int i = 0; i < PatrolRoute.Length; i++)
            {
                Transform point = Child($"Point {i + 1}", route, Vector3.zero);
                point.position = PatrolRoute[i];
                points[i] = point;
            }

            var raptor = root.AddComponent<RaptorAI>();
            WireRef(raptor, "session", session);
            WireRef(raptor, "eyes", eyes);
            WireArray(raptor, "patrolPoints", points);
            return raptor;
        }

        static void BuildThrowables()
        {
            Material material = CreateMaterial("Throwable", new Color(0.75f, 0.72f, 0.65f));
            var group = new GameObject("Throwables").transform;

            var positions = new List<Vector3>(StartingRocks);
            for (int i = 0; i < ScatteredRockCount; i++)
            {
                if (TryFindFreeSpot(0.6f, out Vector3 position))
                    positions.Add(position);
            }

            foreach (Vector3 position in positions)
            {
                var rock = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                rock.name = "Rock";
                rock.transform.SetParent(group);
                rock.transform.position = position + Vector3.up * 0.12f;
                rock.transform.localScale = Vector3.one * 0.22f;
                rock.GetComponent<Renderer>().sharedMaterial = material;
                rock.AddComponent<Rigidbody>().mass = 0.5f;
                rock.AddComponent<Throwable>();
            }
        }

        static GameObject Box(string name, Transform parent, Material material, Vector3 position, Vector3 size)
        {
            var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.name = name;
            box.transform.SetParent(parent);
            box.transform.position = position;
            box.transform.localScale = size;
            box.GetComponent<Renderer>().sharedMaterial = material;
            return box;
        }

        static void Decorate(GameObject part, string name, Transform parent, Material material, Vector3 localPosition, Vector3 scale)
        {
            Object.DestroyImmediate(part.GetComponent<Collider>());
            part.name = name;
            part.transform.SetParent(parent, false);
            part.transform.localPosition = localPosition;
            part.transform.localScale = scale;
            part.GetComponent<Renderer>().sharedMaterial = material;
        }

        static Transform Child(string name, Transform parent, Vector3 localPosition)
        {
            var child = new GameObject(name).transform;
            child.SetParent(parent, false);
            child.localPosition = localPosition;
            return child;
        }

        static Material CreateMaterial(string name, Color colour)
        {
            var material = new Material(Shader.Find("HDRP/Lit"));
            material.SetColor("_BaseColor", colour);
            material.SetFloat("_Smoothness", 0.15f);
            HDMaterial.ValidateMaterial(material);
            AssetDatabase.CreateAsset(material, $"{AssetFolder}/Materials/{name}.mat");
            return material;
        }

        static void Reserve(Vector3 centre, float radius) => occupied.Add((centre, radius));

        static bool TryFindFreeSpot(float radius, out Vector3 position)
        {
            float limit = HalfSize - 2f - radius;
            for (int attempt = 0; attempt < 60; attempt++)
            {
                position = new Vector3(Range(-limit, limit), 0f, Range(-limit, limit));
                if (IsFree(position, radius))
                {
                    Reserve(position, radius);
                    return true;
                }
            }

            position = default;
            return false;
        }

        static bool IsFree(Vector3 position, float radius)
        {
            foreach ((Vector3 centre, float taken) in occupied)
            {
                if (Vector3.Distance(position, centre) < radius + taken)
                    return false;
            }
            return true;
        }

        static float Range(float min, float max) => min + (float)random.NextDouble() * (max - min);

        static void WireRef(Object target, string property, Object value)
        {
            var serialized = new SerializedObject(target);
            Find(serialized, property).objectReferenceValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        static void WireArray(Object target, string property, Object[] values)
        {
            var serialized = new SerializedObject(target);
            SerializedProperty array = Find(serialized, property);
            array.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++)
                array.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        static SerializedProperty Find(SerializedObject serialized, string property)
        {
            return serialized.FindProperty(property)
                ?? throw new System.ArgumentException($"{serialized.targetObject.GetType().Name} has no serialized field '{property}'.");
        }
    }
}
