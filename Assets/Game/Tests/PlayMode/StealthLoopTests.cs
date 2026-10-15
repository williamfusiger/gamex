using System;
using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Gamex.Tests
{
    /// <summary>
    /// Plays the greybox scene and checks the stealth loop end to end: the raptor patrols, reacts
    /// to noise, kills a player it notices, and cannot find one who hid in time.
    /// </summary>
    public class StealthLoopTests
    {
        // The scenarios are simulated faster than real time; waits below are in game seconds.
        const float TimeScale = 4f;

        GameSession session;
        PlayerController player;
        PlayerInteractor interactor;
        RaptorAI raptor;

        [UnitySetUp]
        public IEnumerator LoadGreybox()
        {
            yield return SceneManager.LoadSceneAsync("Greybox", LoadSceneMode.Single);
            yield return null;

            session = Object.FindAnyObjectByType<GameSession>();
            raptor = Object.FindAnyObjectByType<RaptorAI>();
            player = session.Player;
            interactor = player.GetComponent<PlayerInteractor>();
            Time.timeScale = TimeScale;
        }

        [TearDown]
        public void RestoreTimeScale() => Time.timeScale = 1f;

        [UnityTest]
        public IEnumerator RaptorPatrolsWhileThePlayerIsFarAway()
        {
            Vector3 start = raptor.transform.position;

            yield return new WaitForSeconds(8f);

            Assert.AreEqual(RaptorState.Patrol, raptor.State);
            Assert.AreEqual(0f, raptor.Awareness);
            Assert.Greater(Vector3.Distance(start, raptor.transform.position), 2f, "The raptor should have walked along its route.");
        }

        [UnityTest]
        public IEnumerator NoiseWithinEarshotDrawsTheRaptorOver()
        {
            Vector3 source = WalkablePointNear(raptor.transform.position + new Vector3(9f, 0f, 0f));

            NoiseSystem.Emit(source, 14f, NoiseKind.Impact);

            Assert.AreEqual(RaptorState.Investigate, raptor.State);
            yield return WaitFor(() => Vector3.Distance(raptor.transform.position, source) < 2f, 15f, "the raptor to reach the noise");
        }

        [UnityTest]
        public IEnumerator NoiseOutOfEarshotIsIgnored()
        {
            NoiseSystem.Emit(raptor.transform.position + new Vector3(30f, 0f, 0f), 10f, NoiseKind.Impact);
            yield return null;

            Assert.AreEqual(RaptorState.Patrol, raptor.State);
        }

        [UnityTest]
        public IEnumerator RaptorRunsDownAPlayerItNotices()
        {
            bool chased = false;
            raptor.StateChanged += state => chased |= state == RaptorState.Chase;

            player.Teleport(raptor.transform.position + new Vector3(2f, 0f, 0f), 0f);

            yield return WaitFor(() => session.State == SessionState.Dead, 6f, "the raptor to catch the player");
            Assert.IsTrue(chased, "The raptor should have gone through a chase before the kill.");
            Assert.IsTrue(player.ControlsLocked);
        }

        [UnityTest]
        public IEnumerator HidingKeepsThePlayerAliveUntilTheyStepOut()
        {
            HidingSpot spot = Object.FindAnyObjectByType<HidingSpot>();
            Vector3 doorstep = spot.ExitPoint.position;
            player.Teleport(doorstep, 0f);
            player.EnterHiding(spot);

            // Bring the raptor right up to the hiding spot, close enough to notice anyone not hidden.
            Assert.IsTrue(raptor.GetComponent<NavMeshAgent>().Warp(doorstep + spot.ExitPoint.forward * 1.5f));
            NoiseSystem.Emit(doorstep, 10f, NoiseKind.Impact);
            yield return new WaitForSeconds(3f);

            Assert.AreEqual(SessionState.Playing, session.State);
            Assert.AreEqual(RaptorState.Investigate, raptor.State);
            Assert.AreEqual(0f, raptor.Awareness, "A hidden player must not raise awareness.");

            player.ExitHiding();

            yield return WaitFor(() => session.State == SessionState.Dead, 6f, "the raptor to catch the player who stepped out");
        }

        [UnityTest]
        public IEnumerator ThrownRockMakesNoiseWhereItLands()
        {
            NoiseEvent? impact = null;
            void OnNoise(NoiseEvent noise)
            {
                if (noise.Kind == NoiseKind.Impact)
                    impact = noise;
            }

            NoiseSystem.Emitted += OnNoise;
            try
            {
                interactor.Hold(Object.FindAnyObjectByType<Throwable>());
                yield return null;
                interactor.ThrowHeldItem();

                yield return WaitFor(() => impact.HasValue, 8f, "the thrown rock to land");
            }
            finally
            {
                NoiseSystem.Emitted -= OnNoise;
            }

            Assert.IsNull(interactor.HeldItem);
            Assert.Greater(Vector3.Distance(impact.Value.Position, player.transform.position), 5f,
                "The noise should come from where the rock landed, away from the player.");
        }

        [UnityTest]
        public IEnumerator UsingTheRadioEndsTheRunAsRescued()
        {
            Object.FindAnyObjectByType<RescueRadio>().Interact(interactor);
            yield return null;

            Assert.AreEqual(SessionState.Rescued, session.State);
            Assert.IsTrue(player.ControlsLocked);
        }

        static Vector3 WalkablePointNear(Vector3 position)
        {
            Assert.IsTrue(NavMesh.SamplePosition(position, out NavMeshHit hit, 6f, NavMesh.AllAreas), "No navmesh near the test position.");
            return hit.position;
        }

        static IEnumerator WaitFor(Func<bool> condition, float seconds, string what)
        {
            float deadline = Time.time + seconds;
            while (!condition())
            {
                if (Time.time > deadline)
                    Assert.Fail($"Timed out after {seconds} s waiting for {what}.");
                yield return null;
            }
        }
    }
}
