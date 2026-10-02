using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace WowSandbox.EditorTools
{
    /// <summary>
    /// Assigns the textures that Unity's OBJ importer failed to find on WMO exports.
    ///
    /// A WMO's .mtl doesn't bundle its textures: they live in the shared tileset library,
    /// referenced several directories up with Windows separators —
    /// <c>map_Kd ..\..\..\..\dungeons\textures\wood\x.png</c>. The importer leaves those
    /// unresolved, so wow.unity extracts every material with no texture. M2 doodads are
    /// unaffected because their .mtl names a sibling file. And wow.unity only creates each
    /// extracted material once (it skips the file if it exists), so reimporting doesn't help.
    ///
    /// This reads the .mtl itself, resolves each path relative to the OBJ, and sets the
    /// texture on the materials the selection actually renders with.
    /// </summary>
    public static class ObjTextureFixer
    {
        [MenuItem("WoW Sandbox/Fix OBJ Textures in Selection")]
        public static void FixSelection()
        {
            var renderers = new List<Renderer>();
            foreach (var go in Selection.gameObjects)
                renderers.AddRange(go.GetComponentsInChildren<Renderer>(true));

            if (renderers.Count == 0)
            {
                Debug.LogWarning("[ObjTextureFixer] Select the model in the scene (or its prefab) first.");
                return;
            }

            // Every .mtl behind the selection, parsed once: material name -> texture.
            var texturesByMaterial = new Dictionary<string, Texture2D>();
            var missing = new List<string>();
            var parsed = new HashSet<string>();

            foreach (var renderer in renderers)
            {
                string objPath = SourceObjPath(renderer);
                if (objPath == null || !parsed.Add(objPath))
                    continue;

                ReadMtl(objPath, texturesByMaterial, missing);
            }

            int fixedCount = 0;
            var seen = new HashSet<Material>();

            foreach (var renderer in renderers)
            {
                foreach (var material in renderer.sharedMaterials)
                {
                    if (material == null || !seen.Add(material))
                        continue;

                    if (!texturesByMaterial.TryGetValue(material.name, out var texture))
                        continue;

                    Undo.RecordObject(material, "Fix OBJ Textures");
                    bool changed = false;
                    foreach (string property in new[] { "_BaseMap", "_MainTex" })
                    {
                        if (material.HasProperty(property) && material.GetTexture(property) != texture)
                        {
                            material.SetTexture(property, texture);
                            changed = true;
                        }
                    }

                    if (changed)
                    {
                        EditorUtility.SetDirty(material);
                        fixedCount++;
                    }
                }
            }

            AssetDatabase.SaveAssets();

            foreach (string path in missing)
                Debug.LogWarning("[ObjTextureFixer] Texture named in the .mtl isn't in the project: " + path);

            Debug.Log($"[ObjTextureFixer] Assigned textures on {fixedCount} material(s) from " +
                      $"{parsed.Count} .mtl file(s). {missing.Count} texture(s) not found.");
        }

        /// <summary>The .obj asset a renderer's mesh was imported from, or null.</summary>
        static string SourceObjPath(Renderer renderer)
        {
            Mesh mesh = null;
            if (renderer is SkinnedMeshRenderer skinned)
                mesh = skinned.sharedMesh;
            else if (renderer.TryGetComponent(out MeshFilter filter))
                mesh = filter.sharedMesh;

            if (mesh == null)
                return null;

            string path = AssetDatabase.GetAssetPath(mesh);
            return path.EndsWith(".obj", System.StringComparison.OrdinalIgnoreCase) ? path : null;
        }

        static void ReadMtl(string objPath, Dictionary<string, Texture2D> textures, List<string> missing)
        {
            string mtlPath = Path.ChangeExtension(objPath, ".mtl");
            if (!File.Exists(mtlPath))
                return;

            string objDirectory = Path.GetDirectoryName(Path.GetFullPath(objPath));
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string current = null;

            foreach (string raw in File.ReadAllLines(mtlPath))
            {
                string line = raw.Trim();

                if (line.StartsWith("newmtl "))
                {
                    current = line.Substring(7).Trim();
                }
                else if (line.StartsWith("map_Kd ") && current != null)
                {
                    // The separators are the whole problem: normalise before resolving.
                    string relative = line.Substring(7).Trim().Replace('\\', '/');
                    string full = Path.GetFullPath(Path.Combine(objDirectory, relative));

                    if (!full.StartsWith(projectRoot))
                    {
                        missing.Add(relative);
                        continue;
                    }

                    string assetPath = full.Substring(projectRoot.Length + 1).Replace('\\', '/');
                    var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
                    if (texture != null)
                        textures[current] = texture;
                    else
                        missing.Add(assetPath);
                }
            }
        }
    }
}
