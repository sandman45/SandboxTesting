using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace WowSandbox.EditorTools
{
    /// <summary>
    /// Fills the shallows with life: little fish schools (fishbasic01, which is already a
    /// handful of fish in one model) that cruise about as scenery (AmbientSwimmer), and a few
    /// swirls of fish (fishfrenzy) just under the surface that you can fish (FishingSpot) — or
    /// floating ship wreckage with fish round it (tradeskill_fishschool_shipwreck), or a mix.
    ///
    /// Everything goes in the safe water round the island — from the beach out to well short
    /// of DeepSeaDanger's margin — where it's deep enough to swim. Running it again replaces
    /// the previous batch, so tweak the numbers and re-run rather than hand-placing — or tick
    /// Keep existing to add that many more alongside what's there, spaced away from it.
    /// </summary>
    public class SeaLifeSetup : EditorWindow
    {
        const string FishPath = "Assets/WowExports/world/critter/fish/fishbasic01.gltf";
        const string FrenzyPath = "Assets/WowExports/world/expansion01/doodads/generic/fishfrenzy/fishfrenzy.gltf";
        const string WreckagePath = "Assets/WowExports/world/skillactivated/tradeskillenablers/tradeskill_fishschool_shipwreck.gltf";
        const string GeneratedDir = "Assets/WowExports/_Generated";
        const string GroupName = "Sea Life";
        /// <summary>Same -X facing convention as every other M2; see WarriorSetup.ModelYawOffset.</summary>
        const float ModelYawOffset = 90f;

        int _schools = 24;
        int _spots = 5;
        SpotLook _spotLook = SpotLook.Mix;
        float _maxOffshore = 45f;
        Vector2 _fishDepth = new(1f, 4f);
        float _minWaterDepth = 3f;
        float _roamRadius = 20f;
        bool _keepExisting;

        enum SpotLook { FishSwirl, FloatingWreckage, Mix }

        [MenuItem("WoW Sandbox/Add Sea Life")]
        public static void ShowWindow()
        {
            var window = GetWindow<SeaLifeSetup>(true, "Add Sea Life");
            window.minSize = new Vector2(380f, 260f);
            var danger = Object.FindAnyObjectByType<DeepSeaDanger>();
            if (danger != null)
                window._maxOffshore = Mathf.Min(window._maxOffshore, danger.dangerMargin - 10f);
        }

        void OnGUI()
        {
            EditorGUILayout.HelpBox(
                "Fish schools and fishing spots in the shallows round the island. Re-running " +
                "replaces the last batch, unless Keep existing is ticked — then the counts are " +
                "how many more to add.", MessageType.None);

            EditorGUILayout.Space();
            _keepExisting = EditorGUILayout.Toggle("Keep existing", _keepExisting);
            _schools = EditorGUILayout.IntSlider("Fish schools", _schools, 0, 80);
            _spots = EditorGUILayout.IntSlider("Fishing spots", _spots, 0, 20);
            _spotLook = (SpotLook)EditorGUILayout.EnumPopup(new GUIContent("Spot look",
                "Mix makes every other spot floating wreckage."), _spotLook);
            _maxOffshore = EditorGUILayout.Slider("Max distance offshore", _maxOffshore, 0f, 150f);
            _minWaterDepth = EditorGUILayout.Slider("Min water depth", _minWaterDepth, 1f, 20f);
            _fishDepth = MinMax("Fish depth", _fishDepth, 0.5f, 15f);
            _roamRadius = EditorGUILayout.Slider("School roam radius", _roamRadius, 3f, 80f);

            var danger = Object.FindAnyObjectByType<DeepSeaDanger>();
            if (danger != null && _maxOffshore >= danger.dangerMargin)
                EditorGUILayout.HelpBox($"That's past the Dangerous Waters line ({danger.dangerMargin}).", MessageType.Warning);

            EditorGUILayout.Space();
            if (GUILayout.Button("Add", GUILayout.Height(30f)))
                Add();
            if (GUILayout.Button(new GUIContent("Re-seat Floating Wreckage",
                    "Puts every floating wreckage spot already in the scene back on the waterline.")))
                ReseatWreckage();
        }

        /// <summary>
        /// Spots placed before the wreckage kept its own origin were centred on their bounds,
        /// which — with the fish hanging three units under it — lifted the flotsam well clear of
        /// the water. Puts the model's origin back on the spot, on the surface.
        /// </summary>
        static void ReseatWreckage()
        {
            int count = 0;
            foreach (var spot in Object.FindObjectsByType<FishingSpot>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (spot.transform.childCount == 0)
                    continue;
                var model = spot.transform.GetChild(0);
                // Ones added before the Wreckage tick existed are known by their model.
                if (!spot.wreckage && !model.name.Contains("fishschool_shipwreck"))
                    continue;
                Undo.RecordObject(model, "Re-seat Floating Wreckage");
                Undo.RecordObject(spot, "Re-seat Floating Wreckage");
                model.localPosition = Vector3.zero;
                spot.depthBelowSurface = 0f;
                spot.wreckage = true;
                count++;
            }
            if (count > 0)
                EditorSceneManager.MarkAllScenesDirty();
            Debug.Log($"[SeaLifeSetup] Re-seated {count} floating wreckage spot(s) on the waterline. " +
                      (count > 0 ? "Save the scene to keep it." : "None found."));
        }

        static Vector2 MinMax(string label, Vector2 value, float min, float max)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.PrefixLabel(label);
                value.x = EditorGUILayout.FloatField(value.x, GUILayout.Width(50f));
                EditorGUILayout.MinMaxSlider(ref value.x, ref value.y, min, max);
                value.y = EditorGUILayout.FloatField(value.y, GUILayout.Width(50f));
            }
            return value;
        }

        void Add()
        {
            var water = Object.FindAnyObjectByType<WaterVolume>();
            var terrain = Terrain.activeTerrain;
            if (water == null || terrain == null)
            {
                Debug.LogError("[SeaLifeSetup] Needs a Terrain and a WaterVolume (Setup Water).");
                return;
            }

            var fish = _schools > 0 ? Load(FishPath) : null;
            bool swirls = _spots > 0 && _spotLook != SpotLook.FloatingWreckage;
            bool wreckage = _spots > 0 && _spotLook != SpotLook.FishSwirl;
            var frenzy = swirls ? Load(FrenzyPath) : null;
            var floating = wreckage ? Load(WreckagePath) : null;
            if (_schools > 0 && fish == null || swirls && frenzy == null || wreckage && floating == null)
                return;

            var group = GameObject.Find(GroupName);
            if (group != null && !_keepExisting)
            {
                Undo.DestroyObjectImmediate(group);
                group = null;
            }
            if (group == null)
            {
                group = new GameObject(GroupName);
                Undo.RegisterCreatedObjectUndo(group, "Add Sea Life");
            }

            int schools = 0, spots = 0;
            if (fish != null)
            {
                var parent = Child(group, "Fish Schools");
                // Rebuilding the controller deletes the asset, which would strip it from the
                // schools already there — so reuse theirs when keeping them.
                var controller = Existing(parent, $"{GeneratedDir}/fishbasic01") ??
                                 M2AnimatorBuilder.BuildSwimming(FishPath, $"{GeneratedDir}/fishbasic01");
                int first = parent.childCount;
                foreach (var point in Scatter(water, terrain, _schools, 8f, _fishDepth, parent))
                    CreateSchool(fish, controller, point, parent, first + schools++);
            }

            if (frenzy != null || floating != null)
            {
                var parent = Child(group, "Fishing Spots");
                var swirlController = frenzy == null ? null :
                    Existing(parent, $"{GeneratedDir}/fishfrenzy") ?? M2AnimatorBuilder.BuildIdle(FrenzyPath, $"{GeneratedDir}/fishfrenzy");
                var wreckageController = floating == null ? null :
                    Existing(parent, $"{GeneratedDir}/fishschool_shipwreck") ??
                    M2AnimatorBuilder.BuildIdle(WreckagePath, $"{GeneratedDir}/fishschool_shipwreck");
                int first = parent.childCount;
                foreach (var point in Scatter(water, terrain, _spots, 30f, new Vector2(0.8f, 0.8f), parent))
                {
                    bool isWreckage = floating != null && (frenzy == null || spots % 2 == 1);
                    if (isWreckage)
                        CreateSpot(floating, wreckageController, point, parent, first + spots++, 0f, true);
                    else
                        CreateSpot(frenzy, swirlController, point, parent, first + spots++, 0.8f, false);
                }
            }

            Selection.activeGameObject = group;
            EditorSceneManager.MarkSceneDirty(group.scene);
            Debug.Log($"[SeaLifeSetup] Placed {schools}/{_schools} fish schools and {spots}/{_spots} fishing spots. " +
                      "Save the scene to keep them." +
                      (schools < _schools || spots < _spots
                          ? " Fewer than asked — not enough water deep enough that close in; lower Min water depth or raise Max distance offshore."
                          : ""));
        }

        static GameObject Load(string path)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (model == null)
                Debug.LogError($"[SeaLifeSetup] No model at {path}. Export it from wow.export as glTF there.");
            return model;
        }

        /// <summary>The group's child called <paramref name="name"/>, made if it isn't there.</summary>
        static Transform Child(GameObject group, string name)
        {
            var child = group.transform.Find(name);
            if (child != null)
                return child;
            child = new GameObject(name).transform;
            child.SetParent(group.transform);
            return child;
        }

        /// <summary>The animator controller from <paramref name="dir"/> already on something under <paramref name="parent"/>, if any.</summary>
        static RuntimeAnimatorController Existing(Transform parent, string dir)
        {
            foreach (var animator in parent.GetComponentsInChildren<Animator>())
            {
                var controller = animator.runtimeAnimatorController;
                if (controller != null && AssetDatabase.GetAssetPath(controller).StartsWith(dir + "/"))
                    return controller;
            }
            return null;
        }

        /// <summary>
        /// Up to <paramref name="count"/> points in the shallows, at least <paramref name="spacing"/>
        /// apart: in the sea, no further than Max distance offshore, over at least Min water depth,
        /// at a depth below the surface from <paramref name="depth"/> (kept off the bottom), and
        /// spaced away from whatever's already under <paramref name="existing"/>.
        /// </summary>
        List<Vector3> Scatter(WaterVolume water, Terrain terrain, int count, float spacing, Vector2 depth, Transform existing)
        {
            var origin = terrain.GetPosition();
            var size = terrain.terrainData.size;
            var points = new List<Vector3>();
            var taken = new List<Vector3>();
            foreach (Transform child in existing)
                taken.Add(child.position);

            for (int attempt = 0; attempt < count * 200 && points.Count < count; attempt++)
            {
                var point = new Vector3(
                    Random.Range(origin.x - _maxOffshore, origin.x + size.x + _maxOffshore), 0f,
                    Random.Range(origin.z - _maxOffshore, origin.z + size.z + _maxOffshore));
                if (AmbientSwimmer.Offshore(point) > _maxOffshore || !water.Covers(point))
                    continue;

                float floor = Seabed.HeightAt(point, water);
                if (water.SurfaceY - floor < _minWaterDepth)
                    continue;

                point.y = Mathf.Max(water.SurfaceY - Random.Range(depth.x, depth.y), floor + 1f);
                if (!water.Contains(point) || TooClose(taken, point, spacing))
                    continue;

                points.Add(point);
                taken.Add(point);
            }

            return points;
        }

        static bool TooClose(List<Vector3> points, Vector3 point, float spacing)
        {
            foreach (var other in points)
            {
                if ((other - point).sqrMagnitude < spacing * spacing)
                    return true;
            }
            return false;
        }

        void CreateSchool(GameObject model, RuntimeAnimatorController controller, Vector3 position, Transform parent, int index)
        {
            var root = new GameObject($"Fish School_{index:00}");
            root.transform.SetParent(parent);
            root.transform.position = position;
            var instance = Place(model, controller, root);

            var swimmer = root.AddComponent<AmbientSwimmer>();
            swimmer.roamRadius = _roamRadius;
            swimmer.depthRange = _fishDepth;
            swimmer.maxOffshore = _maxOffshore;
            swimmer.cruiseSpeed = Random.Range(1.2f, 2f);
            swimmer.clearance = Mathf.Max(MeasureBounds(instance).extents.y + 0.2f, 0.4f);

            root.transform.rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
        }

        static void CreateSpot(GameObject model, RuntimeAnimatorController controller, Vector3 position, Transform parent, int index,
                               float depthBelowSurface, bool wreckage)
        {
            var root = new GameObject(wreckage ? $"Floating Wreckage_{index:00}" : $"Fishing Spot_{index:00}");
            root.transform.SetParent(parent);
            root.transform.position = position;
            // The wreckage model's origin is its waterline (the fish hang below it), so it keeps
            // that rather than being centred like the swirl.
            var instance = Place(model, controller, root, centre: !wreckage);

            // A generous click target: the swirl is small and moving, and usually half-hidden by waves.
            var bounds = MeasureBounds(instance);
            var sphere = root.AddComponent<SphereCollider>();
            sphere.isTrigger = true;
            sphere.radius = Mathf.Max(bounds.extents.x, bounds.extents.z, 1f) * 1.2f;
            // The swirl rides just under the surface; the wreckage floats half out of it.
            var spot = root.AddComponent<FishingSpot>();
            spot.depthBelowSurface = depthBelowSurface;
            spot.wreckage = wreckage;

            root.transform.rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
        }

        /// <summary>The model under <paramref name="root"/>, facing +Z, body centred on the root (or its own origin there), animated.</summary>
        static GameObject Place(GameObject model, RuntimeAnimatorController controller, GameObject root, bool centre = true)
        {
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(model, root.transform);
            instance.transform.localRotation = Quaternion.Euler(0f, ModelYawOffset, 0f);
            instance.transform.localPosition = Vector3.zero;
            // Moves at runtime, so it mustn't be static-batched in place.
            foreach (var t in instance.GetComponentsInChildren<Transform>(true))
                GameObjectUtility.SetStaticEditorFlags(t.gameObject, 0);

            if (centre)
                instance.transform.position += root.transform.position - MeasureBounds(instance).center;

            if (controller != null)
            {
                var animator = instance.GetComponentInChildren<Animator>();
                if (animator == null)
                    animator = instance.AddComponent<Animator>();
                animator.runtimeAnimatorController = controller;
                animator.applyRootMotion = false;
            }

            return instance;
        }

        static Bounds MeasureBounds(GameObject instance)
        {
            var renderers = instance.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0)
                return new Bounds(instance.transform.position, Vector3.one);

            var bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
                bounds.Encapsulate(renderers[i].bounds);
            return bounds;
        }
    }
}
