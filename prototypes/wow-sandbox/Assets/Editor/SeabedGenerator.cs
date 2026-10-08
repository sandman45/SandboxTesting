using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace WowSandbox.EditorTools
{
    /// <summary>
    /// Builds the sea floor past the terrain's edge (see Seabed). Each vertex outside the
    /// terrain starts at the height of the nearest point on the terrain's border, then
    /// falls away with distance: a gentle shelf for the first stretch, then a steep
    /// drop-off into the abyss near the bottom of the water volume. Vertices under the
    /// terrain sit just below it, out of sight.
    ///
    /// The mesh, texture and material are generated assets in Assets/Terrain (gitignored,
    /// like the terrain itself). Re-run after regenerating the terrain or the water.
    /// </summary>
    public static class SeabedGenerator
    {
        const string ObjectName = "Seabed";
        const string AssetFolder = "Assets/Terrain";
        const float Step = 8f;
        const float ShelfWidth = 120f;
        const float ShelfSlope = 0.12f;
        const float DropSlope = 0.6f;

        [MenuItem("WoW Sandbox/Generate Seabed")]
        static void Generate()
        {
            var terrain = Terrain.activeTerrain;
            var water = Object.FindAnyObjectByType<WaterVolume>();
            if (terrain == null || water == null)
            {
                Debug.LogError("[SeabedGenerator] Needs a Terrain and the water (Generate Terrain, then Setup Water).");
                return;
            }

            var existing = GameObject.Find(ObjectName);
            if (existing != null)
                Undo.DestroyObjectImmediate(existing);

            var centre = water.transform.position;
            var origin = new Vector2(centre.x - water.extents.x, centre.z - water.extents.y);
            int columns = Mathf.CeilToInt(water.extents.x * 2f / Step) + 1;
            int rows = Mathf.CeilToInt(water.extents.y * 2f / Step) + 1;

            var terrainOrigin = terrain.GetPosition();
            var terrainSize = terrain.terrainData.size;
            var terrainRect = new Rect(terrainOrigin.x, terrainOrigin.z, terrainSize.x, terrainSize.z);
            float abyss = water.SurfaceY - water.depth + 5f;

            var heights = new float[columns * rows];
            var vertices = new Vector3[columns * rows];
            var uvs = new Vector2[columns * rows];
            for (int z = 0; z < rows; z++)
            {
                for (int x = 0; x < columns; x++)
                {
                    var point = new Vector3(origin.x + x * Step, 0f, origin.y + z * Step);
                    var flat = new Vector2(point.x, point.z);
                    var nearest = new Vector2(
                        Mathf.Clamp(flat.x, terrainRect.xMin, terrainRect.xMax),
                        Mathf.Clamp(flat.y, terrainRect.yMin, terrainRect.yMax));
                    float border = terrainOrigin.y + terrain.SampleHeight(new Vector3(nearest.x, 0f, nearest.y));
                    float distance = Vector2.Distance(flat, nearest);

                    float height;
                    if (distance <= 0f)
                    {
                        // Under the terrain: tucked below it, never seen.
                        height = border - 2f;
                    }
                    else
                    {
                        float drop = Mathf.Min(distance, ShelfWidth) * ShelfSlope +
                                     Mathf.Max(0f, distance - ShelfWidth) * DropSlope;
                        float ripples = (Mathf.PerlinNoise(point.x * 0.03f, point.z * 0.03f) - 0.5f) * 4f;
                        height = Mathf.Max(border - drop + ripples, abyss);
                    }

                    int i = z * columns + x;
                    heights[i] = height;
                    vertices[i] = new Vector3(point.x, height, point.z);
                    uvs[i] = new Vector2(point.x, point.z) / 12f;
                }
            }

            var triangles = new int[(columns - 1) * (rows - 1) * 6];
            int t = 0;
            for (int z = 0; z < rows - 1; z++)
            {
                for (int x = 0; x < columns - 1; x++)
                {
                    int i = z * columns + x;
                    triangles[t++] = i;
                    triangles[t++] = i + columns;
                    triangles[t++] = i + 1;
                    triangles[t++] = i + 1;
                    triangles[t++] = i + columns;
                    triangles[t++] = i + columns + 1;
                }
            }

            var mesh = new Mesh { name = ObjectName };
            if (vertices.Length > 65000)
                mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.vertices = vertices;
            mesh.uv = uvs;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            System.IO.Directory.CreateDirectory(AssetFolder);
            mesh = SaveAsset(mesh, $"{AssetFolder}/Seabed.asset");
            var material = SaveAsset(BuildMaterial(), $"{AssetFolder}/Seabed.mat");

            var go = new GameObject(ObjectName);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = material;
            go.AddComponent<MeshCollider>().sharedMesh = mesh;
            // Underwater — keep it out of the NavMesh, or wandering NPCs would stroll out to sea.
            go.AddComponent<NavMeshModifier>().ignoreFromBuild = true;

            var seabed = go.AddComponent<Seabed>();
            seabed.heights = heights;
            seabed.columns = columns;
            seabed.rows = rows;
            seabed.origin = origin;
            seabed.step = Step;

            Undo.RegisterCreatedObjectUndo(go, "Generate Seabed");
            EditorSceneManager.MarkSceneDirty(go.scene);
            Selection.activeGameObject = go;
            Debug.Log($"[SeabedGenerator] Built a {columns}x{rows} seabed out to the edge of the water " +
                      $"(abyss at y {abyss:F0}).");
        }

        static T SaveAsset<T>(T asset, string path) where T : Object
        {
            AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(asset, path);
            return AssetDatabase.LoadAssetAtPath<T>(path);
        }

        /// <summary>Mottled wet-sand texture on URP Lit, generated rather than imported.</summary>
        static Material BuildMaterial()
        {
            const int size = 256;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, true) { name = "SeabedSand" };
            var pixels = new Color[size * size];
            var sand = new Color(0.55f, 0.5f, 0.38f);
            var dark = new Color(0.32f, 0.33f, 0.27f);
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float coarse = Mathf.PerlinNoise(x * 0.04f, y * 0.04f);
                    float fine = Mathf.PerlinNoise(x * 0.25f + 100f, y * 0.25f + 100f);
                    pixels[y * size + x] = Color.Lerp(dark, sand, coarse * 0.7f + fine * 0.3f);
                }
            }
            texture.SetPixels(pixels);
            texture.Apply();
            texture = SaveAsset(texture, $"{AssetFolder}/SeabedSand.asset");

            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var material = new Material(shader) { name = "Seabed" };
            material.SetTexture("_BaseMap", texture);
            material.SetTexture("_MainTex", texture);
            material.SetFloat("_Smoothness", 0.15f);
            return material;
        }
    }
}
