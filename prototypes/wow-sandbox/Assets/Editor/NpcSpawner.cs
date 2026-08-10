using System.IO;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

namespace WowSandbox.EditorTools
{
    /// <summary>
    /// Spawns wandering NPCs from any wow.export M2 glTF — warriors, thieves, whatever
    /// else gets exported. Same behaviour as the chickens, sized for the model it's given.
    /// </summary>
    public class NpcSpawner : EditorWindow
    {
        /// <summary>Same -X facing convention as every other M2; see WarriorSetup.ModelYawOffset.</summary>
        const float ModelYawOffset = 90f;

        GameObject _model;
        int _count = 4;
        float _scatterRadius = 12f;
        float _wanderRadius = 15f;
        float _walkSpeed = 1.6f;
        Vector3 _centre = Vector3.zero;

        [MenuItem("WoW Sandbox/Spawn Wandering NPCs")]
        public static void ShowWindow()
        {
            var window = GetWindow<NpcSpawner>(true, "Spawn Wandering NPCs");
            window.minSize = new Vector2(360f, 300f);
        }

        void OnGUI()
        {
            EditorGUILayout.HelpBox(
                "Drag a wow.export .gltf from the Project window into Model. Spawns NPCs that " +
                "walk, pause, and walk again — routing around anything baked into the NavMesh.",
                MessageType.None);

            EditorGUILayout.Space();
            _model = (GameObject)EditorGUILayout.ObjectField("Model (.gltf)", _model, typeof(GameObject), false);

            EditorGUILayout.Space();
            _count = EditorGUILayout.IntSlider("Count", _count, 1, 30);
            _walkSpeed = EditorGUILayout.Slider("Walk speed", _walkSpeed, 0.3f, 6f);
            _scatterRadius = EditorGUILayout.Slider("Scatter radius", _scatterRadius, 1f, 100f);
            _wanderRadius = EditorGUILayout.Slider("Wander radius", _wanderRadius, 1f, 100f);

            EditorGUILayout.Space();
            using (new EditorGUILayout.HorizontalScope())
            {
                _centre = EditorGUILayout.Vector3Field("Centre", _centre);
                using (new EditorGUI.DisabledScope(Selection.activeGameObject == null))
                {
                    if (GUILayout.Button("Use selection", GUILayout.Width(100f), GUILayout.Height(18f)))
                        _centre = Selection.activeGameObject.transform.position;
                }
            }

            EditorGUILayout.Space();
            using (new EditorGUI.DisabledScope(_model == null))
            {
                if (GUILayout.Button("Spawn", GUILayout.Height(30f)))
                    Spawn();
            }
        }

        void Spawn()
        {
            string modelPath = AssetDatabase.GetAssetPath(_model);
            if (string.IsNullOrEmpty(modelPath))
            {
                Debug.LogError("[NpcSpawner] That model isn't an asset on disk.");
                return;
            }

            if (Object.FindFirstObjectByType<NavMeshSurface>() == null)
            {
                Debug.LogError("[NpcSpawner] No NavMeshSurface in the scene. " +
                               "Run WoW Sandbox -> Bake NavMesh first.");
                return;
            }

            string name = Path.GetFileNameWithoutExtension(modelPath);
            var controller = M2AnimatorBuilder.BuildLocomotion(
                modelPath, $"Assets/WowExports/_Generated/{name}");

            var group = new GameObject($"{name}_NPCs");
            Undo.RegisterCreatedObjectUndo(group, "Spawn Wandering NPCs");

            int placed = 0;
            for (int i = 0; i < _count; i++)
            {
                Vector2 offset = Random.insideUnitCircle * _scatterRadius;
                Vector3 candidate = _centre + new Vector3(offset.x, 0f, offset.y);

                if (!NavMesh.SamplePosition(candidate, out NavMeshHit hit, 15f, NavMesh.AllAreas))
                    continue;

                CreateNpc(name, controller, hit.position, group.transform, i);
                placed++;
            }

            if (placed == 0)
            {
                Debug.LogError("[NpcSpawner] No NavMesh near the centre — nothing placed. " +
                               "Check the centre point is over baked terrain.");
                Undo.DestroyObjectImmediate(group);
                return;
            }

            Selection.activeGameObject = group;
            Debug.Log($"[NpcSpawner] Placed {placed} {name} NPC(s) around {_centre}" +
                      (controller == null ? " (no animations — see warning above)." : "."));
        }

        void CreateNpc(string name, RuntimeAnimatorController controller,
                       Vector3 position, Transform parent, int index)
        {
            var root = new GameObject($"{name}_{index:00}");
            root.transform.SetParent(parent);
            root.transform.position = position;
            root.transform.rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(_model, root.transform);
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.Euler(0f, ModelYawOffset, 0f);

            float height = MeasureHeight(instance);

            var agent = root.AddComponent<NavMeshAgent>();
            agent.speed = _walkSpeed;
            agent.angularSpeed = 300f;
            agent.acceleration = 8f;
            agent.height = height;
            agent.radius = Mathf.Max(height * 0.18f, 0.1f);
            agent.stoppingDistance = 0.15f;
            agent.autoBraking = true;

            var wanderer = root.AddComponent<WanderingNpc>();
            wanderer.wanderRadius = _wanderRadius;

            if (controller != null)
            {
                var animator = instance.GetComponentInChildren<Animator>();
                if (animator == null)
                    animator = instance.AddComponent<Animator>();
                animator.runtimeAnimatorController = controller;
                animator.applyRootMotion = false;
            }
        }

        static float MeasureHeight(GameObject instance)
        {
            var renderers = instance.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0)
                return 1.8f;

            var bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
                bounds.Encapsulate(renderers[i].bounds);

            return Mathf.Max(bounds.size.y, 0.1f);
        }
    }
}
