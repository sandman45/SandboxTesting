using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace WowSandbox.EditorTools
{
    /// <summary>
    /// WoW Sandbox → Place Light Source: puts an exported lantern, torch, brazier or campfire
    /// where the Scene view is looking, with a NightLight that lights it up at dusk. Any
    /// export whose name says what it is turns up in the list — the pirate ship's lanterns are
    /// there already; export torches and braziers from wow.export to add them.
    ///
    /// Light sources already in the scene (the ship's own lanterns) need nothing: NightLight
    /// finds them by name at Play.
    /// </summary>
    public class LightSourceSetup : EditorWindow
    {
        const string ExportRoot = "Assets/WowExports";

        [SerializeField] int choice;
        [SerializeField] float height;

        string[] _models;

        [MenuItem("WoW Sandbox/Place Light Source")]
        static void Open()
        {
            var window = GetWindow<LightSourceSetup>(true, "Place Light Source");
            window.minSize = new Vector2(400f, 170f);
        }

        void OnEnable() => _models = FindModels();

        /// <summary>Every exported model named like a light source, preferring the prefab when there's one.</summary>
        internal static string[] FindModels()
        {
            if (!Directory.Exists(ExportRoot))
                return new string[0];
            return Directory.GetFiles(ExportRoot, "*.*", SearchOption.AllDirectories)
                .Where(p => p.EndsWith(".prefab") || p.EndsWith(".gltf") || p.EndsWith(".glb") || p.EndsWith(".obj"))
                .Select(p => p.Replace('\\', '/'))
                .Where(p => !p.Contains("/_Generated/") && NightLight.KindFromName(Path.GetFileNameWithoutExtension(p)) != null)
                .GroupBy(p => Path.ChangeExtension(p, null))
                .Select(g => g.FirstOrDefault(p => p.EndsWith(".prefab")) ?? g.First())
                .OrderBy(p => p)
                .ToArray();
        }

        void OnGUI()
        {
            if (_models == null || _models.Length == 0)
            {
                EditorGUILayout.HelpBox("No light-source models exported. In wow.export, search the models for " +
                                        "lantern, torch, brazier or campfire and export one (glTF) into WowExports.",
                                        MessageType.Warning);
                if (GUILayout.Button("Look again"))
                    _models = FindModels();
                return;
            }

            EditorGUILayout.HelpBox("Placed where the Scene view is looking; it lights up at dusk. Move it where you " +
                                    "want it (a hanging lantern goes under an eave), then save the scene.", MessageType.Info);
            choice = Mathf.Clamp(choice, 0, _models.Length - 1);
            choice = EditorGUILayout.Popup("Model", choice, _models.Select(Path.GetFileNameWithoutExtension).ToArray());
            height = EditorGUILayout.FloatField(new GUIContent("Height",
                "How tall it stands, in world units (the warrior is about 1.9). 0 keeps the model's own size."), height);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Place", GUILayout.Height(28f)))
                Place(_models[choice], height);
            if (GUILayout.Button("Look again", GUILayout.Height(28f), GUILayout.Width(100f)))
                _models = FindModels();
            EditorGUILayout.EndHorizontal();
        }

        static void Place(string modelPath, float height)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            if (prefab == null)
            {
                Debug.LogWarning($"[LightSource] Couldn't load {modelPath}.");
                return;
            }
            if (!InteractableSetup.Ground(out var point, out var facing))
                return;

            var placed = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            Undo.RegisterCreatedObjectUndo(placed, "Place Light Source");
            placed.name = Path.GetFileNameWithoutExtension(modelPath);
            placed.transform.SetPositionAndRotation(point, facing);

            var bounds = InteractableSetup.RendererBounds(placed);
            if (height > 0f && bounds.size.y > 0.01f)
                placed.transform.localScale *= height / bounds.size.y;
            // Sat on the ground by its lowest point, not its pivot.
            bounds = InteractableSetup.RendererBounds(placed);
            placed.transform.position += Vector3.up * (point.y - bounds.min.y);

            var kind = NightLight.KindFromName(placed.name) ?? NightLight.Kind.Lantern;
            placed.AddComponent<NightLight>().kind = kind;

            Selection.activeGameObject = placed;
            EditorSceneManager.MarkSceneDirty(placed.scene);
            if (Application.isPlaying)
                Debug.LogWarning("[LightSource] Placed during Play mode — Unity discards it on exit. Place it in Edit mode to keep it.");
            Debug.Log($"[LightSource] Placed a {kind.ToString().ToLowerInvariant()}. Its light comes on at dusk; set Light " +
                      "Offset on its NightLight if the light should sit somewhere else. Save the scene.", placed);
        }
    }
}
