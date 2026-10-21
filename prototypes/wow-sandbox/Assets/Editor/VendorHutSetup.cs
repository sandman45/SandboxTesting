using System.IO;
using System.Linq;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;

namespace WowSandbox.EditorTools
{
    /// <summary>
    /// WoW Sandbox → Place Vendor Hut: puts an exported hut where the Scene view is looking,
    /// with mesh colliders, and a Bed (a BedSpot) inside it that the vendor walks to at bedtime
    /// (Vendor.bed). Drag the hut and its Bed where you want them, then Bake and Check: that
    /// rebakes the NavMesh and tests that the vendor can walk from the stall to the bed.
    /// </summary>
    public class VendorHutSetup : EditorWindow
    {
        const string ExportRoot = "Assets/WowExports";

        [SerializeField] string filter = "";
        [SerializeField] int choice;
        [SerializeField] float height;

        string[] _models;

        [MenuItem("WoW Sandbox/Place Vendor Hut")]
        static void Open()
        {
            var window = GetWindow<VendorHutSetup>(true, "Place Vendor Hut");
            window.minSize = new Vector2(420f, 230f);
        }

        void OnEnable() => _models = FindModels();

        /// <summary>Every exported model, preferring the prefab when there's one.</summary>
        static string[] FindModels()
        {
            if (!Directory.Exists(ExportRoot))
                return new string[0];
            return Directory.GetFiles(ExportRoot, "*.*", SearchOption.AllDirectories)
                .Where(p => p.EndsWith(".prefab") || p.EndsWith(".gltf") || p.EndsWith(".glb") || p.EndsWith(".obj"))
                .Select(p => p.Replace('\\', '/'))
                .Where(p => !p.Contains("/_Generated/") && !p.Contains("/character/") && !p.Contains("/item/"))
                .GroupBy(p => Path.ChangeExtension(p, null))
                .Select(g => g.FirstOrDefault(p => p.EndsWith(".prefab")) ?? g.First())
                .OrderBy(p => p)
                .ToArray();
        }

        void OnGUI()
        {
            var vendor = FindFirstObjectByType<Vendor>();
            if (vendor == null)
            {
                EditorGUILayout.HelpBox("No vendor in the scene — WoW Sandbox → Add Vendor first.", MessageType.Warning);
                return;
            }

            EditorGUILayout.HelpBox($"Placed where the Scene view is looking. Move the hut, and its Bed inside it, then " +
                                    $"Bake and Check. {vendor.name} walks to the Bed at bedtime and back at dawn.",
                                    MessageType.Info);
            filter = EditorGUILayout.TextField(new GUIContent("Search", "Part of the model's name or path: hut, tent, shack…"), filter);
            var shown = _models.Where(p => string.IsNullOrEmpty(filter) ||
                                           p.ToLowerInvariant().Contains(filter.ToLowerInvariant())).ToArray();
            if (shown.Length == 0)
            {
                EditorGUILayout.HelpBox("No exported model matches. Export the hut (glTF) into WowExports, then Look again.",
                                        MessageType.Warning);
            }
            else
            {
                choice = Mathf.Clamp(choice, 0, shown.Length - 1);
                choice = EditorGUILayout.Popup("Model", choice, shown.Select(Path.GetFileNameWithoutExtension).ToArray());
                height = EditorGUILayout.FloatField(new GUIContent("Height",
                    "How tall it stands, in world units (the warrior is about 1.9). 0 keeps the model's own size."), height);
            }

            EditorGUILayout.BeginHorizontal();
            using (new EditorGUI.DisabledScope(shown.Length == 0))
            {
                if (GUILayout.Button("Place", GUILayout.Height(28f)))
                    Place(vendor, shown[choice], height);
            }
            if (GUILayout.Button("Look again", GUILayout.Height(28f), GUILayout.Width(100f)))
                _models = FindModels();
            EditorGUILayout.EndHorizontal();

            using (new EditorGUI.DisabledScope(vendor.bed == null))
            {
                if (GUILayout.Button("Bake and Check", GUILayout.Height(28f)))
                    BakeAndCheck(vendor);
            }
            if (vendor.bed != null && GUILayout.Button("Select Bed"))
                Selection.activeTransform = vendor.bed;
        }

        static void Place(Vendor vendor, string modelPath, float height)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            if (prefab == null)
            {
                Debug.LogWarning($"[VendorHut] Couldn't load {modelPath}.");
                return;
            }
            if (!InteractableSetup.Ground(out var point, out var facing))
                return;

            var hut = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            Undo.RegisterCreatedObjectUndo(hut, "Place Vendor Hut");
            hut.name = $"{vendor.name}'s Hut";
            hut.transform.SetPositionAndRotation(point, facing);

            var bounds = InteractableSetup.RendererBounds(hut);
            if (height > 0f && bounds.size.y > 0.01f)
                hut.transform.localScale *= height / bounds.size.y;
            bounds = InteractableSetup.RendererBounds(hut);
            hut.transform.position += Vector3.up * (point.y - bounds.min.y);
            MeshColliderTool.AddCollidersTo(hut);

            var bed = new GameObject("Bed", typeof(BedSpot));
            Undo.RegisterCreatedObjectUndo(bed, "Place Vendor Hut");
            bed.transform.SetParent(hut.transform, false);
            bed.transform.position = Floor(InteractableSetup.RendererBounds(hut));
            bed.transform.rotation = facing;

            Undo.RecordObject(vendor, "Place Vendor Hut");
            if (vendor.bed != null && vendor.bed.GetComponentInParent<BedSpot>() != null)
                Debug.Log($"[VendorHut] {vendor.name} had a bed already ({vendor.bed.name}); the new hut's replaces it. " +
                          "Delete the old hut if you don't want it.", vendor.bed);
            vendor.bed = bed.transform;
            EditorUtility.SetDirty(vendor);

            Selection.activeGameObject = hut;
            EditorSceneManager.MarkSceneDirty(hut.scene);
            if (Application.isPlaying)
                Debug.LogWarning("[VendorHut] Placed during Play mode — Unity discards it on exit. Place it in Edit mode to keep it.");
            Debug.Log($"[VendorHut] Placed {Path.GetFileNameWithoutExtension(modelPath)} with a Bed inside. Move them where " +
                      "you want, then Bake and Check, and save the scene.", hut);
        }

        /// <summary>The floor in the middle of the hut: the highest surface below its centre, or the ground under it.</summary>
        static Vector3 Floor(Bounds bounds)
        {
            Physics.SyncTransforms();
            var centre = bounds.center;
            var from = new Vector3(centre.x, bounds.min.y + bounds.size.y * 0.5f, centre.z);
            return Physics.Raycast(from, Vector3.down, out var hit, bounds.size.y, ~0, QueryTriggerInteraction.Ignore)
                ? hit.point
                : new Vector3(centre.x, bounds.min.y, centre.z);
        }

        static void BakeAndCheck(Vendor vendor)
        {
            // He has no NavMeshAgent until Play, so the bake would cut a hole round him where he stands.
            if (!vendor.TryGetComponent<NavMeshModifier>(out var modifier))
                modifier = Undo.AddComponent<NavMeshModifier>(vendor.gameObject);
            modifier.ignoreFromBuild = true;
            ChickenSetup.BakeNavMesh();

            var from = vendor.transform.position;
            var to = vendor.bed.position;
            bool fromOk = NavMesh.SamplePosition(from, out var start, 1.5f, NavMesh.AllAreas);
            bool toOk = NavMesh.SamplePosition(to, out var end, 1f, NavMesh.AllAreas);
            if (!fromOk)
            {
                Debug.LogWarning($"[VendorHut] No NavMesh under {vendor.name} at the stall — he can't set off. Move him " +
                                 "clear of the stall's mesh, or check the stall isn't baked as solid ground.", vendor);
                return;
            }
            if (!toOk)
            {
                Debug.LogWarning("[VendorHut] No NavMesh at the Bed. Is it on the hut's floor? The floor may be too " +
                                 "small, or too far above the terrain, to bake; move the Bed and try again.", vendor.bed);
                return;
            }

            var path = new NavMeshPath();
            NavMesh.CalculatePath(start.position, end.position, NavMesh.AllAreas, path);
            if (path.status == NavMeshPathStatus.PathComplete)
                Debug.Log($"[VendorHut] {vendor.name} can walk from the stall to bed. Save the scene.", vendor.bed);
            else
                Debug.LogWarning($"[VendorHut] No full path from the stall to the Bed ({path.status}) — the doorway may be " +
                                 "too narrow for the NavMesh, or blocked. Move the Bed nearer the door, or scale the hut up.",
                                 vendor.bed);
        }
    }
}
