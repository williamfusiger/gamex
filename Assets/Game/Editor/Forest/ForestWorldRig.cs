using System;
using System.Collections.Generic;
using System.Globalization;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

namespace Gamex.EditorTools
{
    /// <summary>
    /// Turns the plan into scene objects and adds what makes the forest a level: the invisible walls,
    /// the gameplay anchors, the NavMesh and the player. Each method builds one branch of the
    /// hierarchy of spec section 3.7, and the scene builder calls them in the order of section 11.5.
    /// </summary>
    public static class ForestWorldRig
    {
        /// <summary>
        /// One prefab instance per placement, named after its index in the plan so the audit can match
        /// the two. A prefab that is not built yet is skipped, which is what makes every milestone a
        /// working scene: the first has no kit at all, the second no trees.
        /// </summary>
        public static int Instantiate(ForestPlan plan, Transform environment)            // returns instances created; skips classes whose prefabs are missing; throws if !plan.grounded
        {
            if (plan == null) throw new ArgumentNullException(nameof(plan));
            if (environment == null) throw new ArgumentNullException(nameof(environment));
            if (!plan.grounded)
                throw new InvalidOperationException("ForestWorldRig.Instantiate needs a grounded plan: call ForestScatter.Ground after ForestTerrain.AddRootMounds.");

            Transform cells = Child("Cells", environment);
            Transform landmarks = Child("Landmarks", environment);
            var cellRoots = new Transform[ForestLayout.CellsPerSide, ForestLayout.CellsPerSide];
            var landmarkRoots = new Dictionary<string, Transform>();
            foreach (ForestLayout.Landmark landmark in ForestLayout.Landmarks)
                landmarkRoots.Add(landmark.name, Child(landmark.name, landmarks));

            var prefabs = new Dictionary<string, GameObject>();
            int classes = Enum.GetValues(typeof(ForestClass)).Length;
            int[] created = new int[classes], skipped = new int[classes];

            for (int i = 0; i < plan.placements.Length; i++)
            {
                ForestPlacement placement = plan.placements[i];
                if (!prefabs.TryGetValue(placement.prefabId, out GameObject prefab))
                {
                    prefab = ForestPrefabs.Exists(placement.prefabId) ? ForestPrefabs.Load(placement.prefabId) : null;
                    prefabs.Add(placement.prefabId, prefab);
                }
                if (prefab == null)
                {
                    skipped[(int)placement.cls]++;
                    continue;
                }

                Transform parent;
                if (string.IsNullOrEmpty(placement.landmark))
                {
                    int cellX = Mathf.Clamp(placement.cellX, 0, ForestLayout.CellsPerSide - 1);
                    int cellZ = Mathf.Clamp(placement.cellZ, 0, ForestLayout.CellsPerSide - 1);
                    parent = cellRoots[cellX, cellZ] ?? (cellRoots[cellX, cellZ] = Child($"Cell_{cellX}_{cellZ}", cells));
                }
                else if (!landmarkRoots.TryGetValue(placement.landmark, out parent))
                {
                    parent = Child(placement.landmark, landmarks);
                    landmarkRoots.Add(placement.landmark, parent);
                }

                // InstantiatePrefab returns UnityEngine.Object; it keeps the link to the prefab asset.
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                instance.name = placement.prefabId + "__" + i.ToString("00000", CultureInfo.InvariantCulture);
                instance.transform.SetParent(parent, false);
                instance.transform.SetPositionAndRotation(placement.position, placement.rotation);
                instance.transform.localScale = Vector3.one * placement.scale;
                // A LODGroup's switch distances grow with the instance's scale. The pole-stage young firs of review 2 of the M4
                // captures (ForestLayout.YoungFirMidShare, 1.40 to 1.55) would draw their needle level to 20 m and more; their group
                // size is held so that they switch where a fir of the largest regular scale (YoungFirMaxScale) does.
                if (placement.cls == ForestClass.YoungFir && placement.scale > ForestLayout.YoungFirMaxScale)
                {
                    var group = instance.GetComponent<LODGroup>();
                    if (group != null) group.size *= ForestLayout.YoungFirMaxScale / placement.scale;
                }
                if (placement.tint != 0)
                    ForestPrefabs.ApplyTint(instance, placement.tint);
                created[(int)placement.cls]++;
            }

            int total = 0;
            for (int cls = 0; cls < classes; cls++)
            {
                total += created[cls];
                if (skipped[cls] > 0)
                    Debug.Log($"ForestWorldRig: skipped {skipped[cls]} {(ForestClass)cls} placements whose prefabs are not built yet ({created[cls]} placed).");
            }
            Debug.Log($"ForestWorldRig: {total} instances from {plan.placements.Length} placements.");
            return total;
        }

        /// <summary>
        /// The physical edge: 24 invisible boxes just outside the boundary, one per straight side and
        /// five chords per corner arc. Neighbours overlap, so there is no slit to slip through.
        /// </summary>
        public static void BuildBounds(Transform environment)
        {
            if (environment == null) throw new ArgumentNullException(nameof(environment));

            Transform bounds = Child("Bounds", environment);
            Terrain terrain = environment.GetComponentInChildren<Terrain>();
            float straightX = ForestLayout.HalfExtentX - ForestLayout.CornerRadius;
            float straightZ = ForestLayout.HalfExtentZ - ForestLayout.CornerRadius;
            float wallX = ForestLayout.HalfExtentX + ForestLayout.WallOffset;
            float wallZ = ForestLayout.HalfExtentZ + ForestLayout.WallOffset;

            Wall("North", bounds, terrain, new Vector2(-straightX, wallZ), new Vector2(straightX, wallZ));
            Wall("South", bounds, terrain, new Vector2(-straightX, -wallZ), new Vector2(straightX, -wallZ));
            Wall("East", bounds, terrain, new Vector2(wallX, -straightZ), new Vector2(wallX, straightZ));
            Wall("West", bounds, terrain, new Vector2(-wallX, -straightZ), new Vector2(-wallX, straightZ));

            string[] names = { "NorthEast", "NorthWest", "SouthWest", "SouthEast" };
            float[] signX = { 1f, -1f, -1f, 1f }, signZ = { 1f, 1f, -1f, -1f };
            float radius = ForestLayout.CornerRadius + ForestLayout.WallOffset;
            float step = 90f / ForestLayout.WallChordsPerCorner * Mathf.Deg2Rad;
            for (int corner = 0; corner < names.Length; corner++)
            {
                var centre = new Vector2(signX[corner] * straightX, signZ[corner] * straightZ);
                for (int chord = 0; chord < ForestLayout.WallChordsPerCorner; chord++)
                {
                    float from = chord * step, to = (chord + 1) * step;
                    Wall($"{names[corner]}_{chord}", bounds, terrain,
                        centre + new Vector2(signX[corner] * Mathf.Cos(from), signZ[corner] * Mathf.Sin(from)) * radius,
                        centre + new Vector2(signX[corner] * Mathf.Cos(to), signZ[corner] * Mathf.Sin(to)) * radius);
                }
            }
        }

        /// <summary>
        /// The Gameplay root of section 3.7: empty transforms only. Waypoints every 10 m along the
        /// trail, a patrol node per control point of the three trails, three dinosaur spawns, and the
        /// capture viewpoints with their position and rotation.
        /// </summary>
        public static void BuildGameplayAnchors(Terrain terrain)                         // the Gameplay root of section 3.7
        {
            if (terrain == null) throw new ArgumentNullException(nameof(terrain));

            Transform root = new GameObject("Gameplay").transform;

            Transform waypoints = Child("TrailWaypoints", root);
            float length = ForestLayout.SplineLength(ForestLayout.Trail);
            int index = 0;
            for (float metres = 0f; metres < length - 1f; metres += ForestLayout.WaypointSpacing)
                Waypoint(waypoints, terrain, index++, metres);
            // The north end is always the last waypoint, whatever the spacing leaves over.
            Waypoint(waypoints, terrain, index, length);

            Transform patrol = Child("PatrolNodes", root);
            for (int spline = 0; spline < ForestLayout.AllTrails.Length; spline++)
            {
                Vector2[] points = ForestLayout.AllTrails[spline];
                for (int n = 0; n < points.Length; n++)
                    Anchor($"{ForestLayout.AllTrailNames[spline]}_{n}", patrol, OnGround(terrain, points[n], 0f), Quaternion.identity);
            }

            Transform spawns = Child("DinoSpawns", root);
            foreach (ForestLayout.Landmark spawn in ForestLayout.DinoSpawns)
                Anchor(spawn.name, spawns, OnGround(terrain, spawn.xz, 0f), Quaternion.identity);

            Transform viewpoints = Child("Viewpoints", root);
            foreach (ForestLayout.Viewpoint view in ForestLayout.Viewpoints)
            {
                Vector3 position = view.heightIsAboveGround
                    ? OnGround(terrain, new Vector2(view.position.x, view.position.z), view.position.y)
                    : view.position;
                Anchor(view.id, viewpoints, position, Quaternion.Euler(view.euler));
            }
        }

        /// <summary>
        /// The greybox bake, unchanged: one surface on Environment that collects the physics colliders
        /// of its children, which here are the terrain, the trunk capsules, the collision meshes and
        /// the walls.
        /// </summary>
        public static void BakeNavMesh(GameObject environment)
        {
            if (environment == null) throw new ArgumentNullException(nameof(environment));

            var surface = environment.AddComponent<NavMeshSurface>();
            surface.collectObjects = CollectObjects.Children;
            surface.useGeometry = UnityEngine.AI.NavMeshCollectGeometry.PhysicsColliders;
            Physics.SyncTransforms();
            surface.BuildNavMesh();
            ForestTerrain.EnsureAssetFolder(ForestPaths.DataFolder);
            AssetDatabase.CreateAsset(surface.navMeshData, ForestPaths.NavMeshPath);
        }

        /// <summary>
        /// A copy of the greybox player (GreyboxSceneBuilder.BuildPlayer, which is private), plus the torch the player carries at
        /// night (N1: CameraPivot/Flashlight, ForestFlashlight). A test compares the two rigs, so this copy cannot drift; the torch
        /// is the one difference it allows.
        /// </summary>
        public static GameObject BuildPlayer(Vector3 position, float yaw)                // returns the Player root
        {
            var root = new GameObject("Player");
            // Set on the transform: PlayerController.Teleport needs a field that Awake assigns, and Awake
            // does not run in edit mode. The controller reads its yaw from the transform when play starts.
            root.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));

            var body = root.AddComponent<CharacterController>();
            body.height = 1.8f;
            body.radius = 0.35f;
            body.center = new Vector3(0f, 0.9f, 0f);

            Transform pivot = Child("CameraPivot", root.transform, new Vector3(0f, 1.65f, 0f));
            pivot.gameObject.tag = "MainCamera";
            var camera = pivot.gameObject.AddComponent<Camera>();
            camera.nearClipPlane = 0.05f;
            camera.farClipPlane = 400f;
            camera.fieldOfView = 70f;
            pivot.gameObject.AddComponent<HDAdditionalCameraData>();
            pivot.gameObject.AddComponent<AudioListener>();

            Transform holdPoint = Child("HoldPoint", pivot, new Vector3(0.35f, -0.3f, 0.7f));
            ForestFlashlight.Build(pivot);

            root.AddComponent<PlayerInputReader>();
            var controller = root.AddComponent<PlayerController>();
            var interactor = root.AddComponent<PlayerInteractor>();
            WireRef(controller, "cameraPivot", pivot);
            WireRef(interactor, "holdPoint", holdPoint);
            return root;
        }

        /// <summary>The session object that locks the cursor. No HUD: the view stays clean for judging the scenery.</summary>
        public static GameObject BuildSession(GameObject player)
        {
            if (player == null) throw new ArgumentNullException(nameof(player));
            var controller = player.GetComponent<PlayerController>();
            if (controller == null)
                throw new ArgumentException("The player object has no PlayerController.", nameof(player));

            var root = new GameObject("Game");
            var session = root.AddComponent<GameSession>();
            WireRef(session, "player", controller);
            return root;
        }

        // ---- Bounds ----

        static void Wall(string name, Transform parent, Terrain terrain, Vector2 from, Vector2 to)
        {
            Vector2 along = to - from;
            float length = along.magnitude;
            Vector2 direction = along / length;
            Vector2 across = ForestLayout.RightOf(direction);

            // The lowest ground under the wall decides where it stands, so no gap opens beneath it. The
            // ground along one wall varies by a few metres at most, which leaves it tall everywhere.
            float lowest = float.MaxValue;
            int steps = Mathf.CeilToInt(length);
            for (int i = 0; i <= steps; i++)
            {
                Vector2 onLine = Vector2.Lerp(from, to, i / (float)steps);
                for (int side = -1; side <= 1; side++)
                    lowest = Mathf.Min(lowest, GroundHeight(terrain, onLine + across * (side * ForestLayout.WallThickness * 0.5f)));
            }

            var wall = new GameObject(name);
            wall.transform.SetParent(parent, false);
            Vector2 middle = (from + to) * 0.5f;
            // A box's long side is its local X; this yaw turns local X onto the wall's direction.
            float yaw = Mathf.Atan2(-direction.y, direction.x) * Mathf.Rad2Deg;
            wall.transform.SetPositionAndRotation(
                new Vector3(middle.x, lowest - 0.5f + ForestLayout.WallHeight * 0.5f, middle.y), Quaternion.Euler(0f, yaw, 0f));

            var box = wall.AddComponent<BoxCollider>();
            // Longer than the chord by the wall's thickness, so it overlaps both neighbours.
            box.size = new Vector3(length + ForestLayout.WallThickness, ForestLayout.WallHeight, ForestLayout.WallThickness);
        }

        // ---- Anchors ----

        static void Waypoint(Transform parent, Terrain terrain, int index, float metres)
        {
            Vector2 point = ForestLayout.PointAtDistance(ForestLayout.Trail, metres, out Vector2 tangent);
            Anchor("WP_" + index.ToString("00", CultureInfo.InvariantCulture), parent, OnGround(terrain, point, 0f),
                Quaternion.Euler(0f, ForestLayout.Bearing(tangent), 0f));
        }

        static void Anchor(string name, Transform parent, Vector3 position, Quaternion rotation)
        {
            Transform anchor = Child(name, parent);
            anchor.SetPositionAndRotation(position, rotation);
        }

        static Vector3 OnGround(Terrain terrain, Vector2 xz, float heightAboveGround)
        {
            return new Vector3(xz.x, GroundHeight(terrain, xz) + heightAboveGround, xz.y);
        }

        // SampleHeight is relative to the terrain object, which sits 20 m below the origin.
        static float GroundHeight(Terrain terrain, Vector2 xz)
        {
            return terrain != null ? terrain.SampleHeight(new Vector3(xz.x, 0f, xz.y)) + terrain.transform.position.y : 0f;
        }

        // ---- Small helpers, as in the greybox builder ----

        static Transform Child(string name, Transform parent)
        {
            return Child(name, parent, Vector3.zero);
        }

        static Transform Child(string name, Transform parent, Vector3 localPosition)
        {
            var child = new GameObject(name).transform;
            child.SetParent(parent, false);
            child.localPosition = localPosition;
            return child;
        }

        static void WireRef(UnityEngine.Object target, string property, UnityEngine.Object value)
        {
            var serialized = new SerializedObject(target);
            SerializedProperty found = serialized.FindProperty(property)
                ?? throw new ArgumentException($"{target.GetType().Name} has no serialized field '{property}'.");
            found.objectReferenceValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
