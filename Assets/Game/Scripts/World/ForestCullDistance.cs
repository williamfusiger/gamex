using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Gamex
{
    /// <summary>
    /// W1 I2b (owner's decision of 2026-10-17, issues/W1-runtime.md section 11): at night only, the forest is culled nearer than the kit's
    /// own LOD tables say (trees and logs about 55 m at scale 1, young firs 50 m), never nearer than 40 m. One per lighting preset object,
    /// enabled with its preset: Night carries the shortened distance, OvercastMist 0 (the kit's). Every LODGroup of the scene whose last
    /// level ends beyond the distance (in metres from the camera, for the player's 70 degree field of view, as the kit computes them) ends
    /// there instead; a level that ended beyond it shrinks to a sliver just short of it, so no level changes where it is drawn nearer than
    /// the distance. Disabling restores every group. Not [ExecuteAlways]: ForestCapture applies the same distance for its before-and-after
    /// captures through <see cref="Apply"/>.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ForestCullDistance : MonoBehaviour
    {
        /// <summary>The kit's screen heights assume this half field of view (ForestKitSpec.TanHalfFieldOfView, tan 35 degrees).</summary>
        public const float TanHalfFieldOfView = 0.70020754f;
        /// <summary>The owner's floor: the forest is never culled nearer than this.</summary>
        public const float Minimum = 40f;

        [SerializeField] float distance;   // metres; 0 = the kit's own cull distances

        Restore applied;

        /// <summary>The shortened cull distance (m), or 0 for the kit's own.</summary>
        public float Distance => distance;

        /// <summary>The LOD levels of every group changed, to put back.</summary>
        public sealed class Restore
        {
            internal readonly List<(LODGroup group, LOD[] lods)> groups = new List<(LODGroup, LOD[])>();
            /// <summary>LOD groups changed.</summary>
            public int Count => groups.Count;

            /// <summary>Every changed group back to its own levels.</summary>
            public void Undo()
            {
                foreach ((LODGroup group, LOD[] lods) in groups)
                    if (group != null)
                        SetLevels(group, lods);
                groups.Clear();
            }
        }

        // Play mode is entered without a domain reload in this project: nothing static to reset.

        void OnEnable()
        {
            if (distance > 0f)
                applied = Apply(gameObject.scene, distance);
        }

        void OnDisable()
        {
            applied?.Undo();
            applied = null;
        }

        /// <summary>
        /// Every LOD group of the scene ends at <paramref name="metres"/> (at least <see cref="Minimum"/>) where it ended beyond it.
        /// Returns what to undo.
        /// </summary>
        public static Restore Apply(Scene scene, float metres)
        {
            var restore = new Restore();
            float cull = Mathf.Max(Minimum, metres);
            if (!scene.IsValid())
                return restore;
            var groups = new List<LODGroup>();
            foreach (GameObject root in scene.GetRootGameObjects())
                groups.AddRange(root.GetComponentsInChildren<LODGroup>(true));
            foreach (LODGroup group in groups)
            {
                LOD[] lods = group.GetLODs();
                if (lods.Length == 0)
                    continue;
                Vector3 scale = group.transform.lossyScale;
                float worldSize = group.size * Mathf.Max(Mathf.Abs(scale.x), Mathf.Max(Mathf.Abs(scale.y), Mathf.Abs(scale.z)));
                float Until(float height) => height > 0f ? worldSize / (2f * TanHalfFieldOfView * height) : float.PositiveInfinity;
                int last = lods.Length - 1;
                if (!(Until(lods[last].screenRelativeTransitionHeight) > cull))
                    continue;
                var changed = (LOD[])lods.Clone();
                float atCull = worldSize / (2f * TanHalfFieldOfView * cull);
                // Levels that end beyond the distance: the last ends at it, the others in slivers just short of it, in order.
                for (int i = last; i >= 0; i--)
                {
                    if (!(Until(lods[i].screenRelativeTransitionHeight) > cull))
                        break;
                    changed[i].screenRelativeTransitionHeight = atCull * (1f + 0.002f * (last - i));
                }
                // Heights must fall from level to level; a level that now ends at or beyond its predecessor keeps the order.
                bool valid = true;
                for (int i = 1; i < changed.Length; i++)
                    if (!(changed[i].screenRelativeTransitionHeight < changed[i - 1].screenRelativeTransitionHeight))
                        valid = false;
                if (!valid)
                    continue;
                restore.groups.Add((group, lods));
                SetLevels(group, changed);
            }
            return restore;
        }

        // SetLODs recalculates the group's bounds (ForestPrefabs sets the size after it for that reason): a larger size moves every
        // transition outward, and the Resident Drawer then drew 35 to 38 M triangles at pano_90 instead of 21.5 M (w1-i2b it2). The size
        // and the reference point are put back.
        static void SetLevels(LODGroup group, LOD[] lods)
        {
            float size = group.size;
            Vector3 reference = group.localReferencePoint;
            group.SetLODs(lods);
            group.size = size;
            group.localReferencePoint = reference;
        }
    }
}
