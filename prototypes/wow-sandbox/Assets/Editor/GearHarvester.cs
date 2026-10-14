using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace WowSandbox.EditorTools
{
    /// <summary>
    /// Turns wow.export character exports into GearLooks the player can wear. Export the
    /// player's model once with nothing on (the base folder) and then once per item, wearing
    /// only that item, renaming each export's folder afterwards (wow.export always writes this
    /// model to thinhuman/male). Each piece folder is compared against the base:
    /// - body texture pixels that differ become the item's painted-on layer (exact: layering
    ///   shirt, shorts and sandals onto the bare body reproduces an export wearing all three
    ///   pixel for pixel);
    /// - body pieces it has that the base doesn't are added (and their meshes kept), ones the
    ///   base has that it doesn't are hidden;
    /// - its "…_Item…" meshes are the item's own: skinned ones are kept as they are; rigid ones
    ///   (helms, weapons, shields, shoulders) go on WoW's own attachment point for their slot,
    ///   read from a raw export of the model (the raw folder: thinhumanmale.m2 and its .skel —
    ///   see M2Attachments). Without one, only helms are placed, by HelmFitter's guess.
    /// Re-run after adding a piece folder; it rebuilds the whole library and hands it to the
    /// player in the scene.
    /// </summary>
    public static class GearHarvester
    {
        const string ExportRoot = "Assets/WowExports/character/thinhuman";
        const string BaseFolder = "base";
        /// <summary>wow.export's raw M2 export of the same model (Models tab, Export M2 / WMO (Raw), with .skel files).</summary>
        const string RawFolder = "raw";
        const string OutputRoot = "Assets/WowExports/_Generated/Gear";
        const string LibraryPath = OutputRoot + "/GearLibrary.asset";

        /// <summary>Piece folder → the catalog item it dresses. Folders not listed here are skipped.</summary>
        static readonly (string folder, string item)[] Pieces =
        {
            ("shirt", "Linen Shirt"),
            ("shorts", "Cloth Shorts"),
            ("sandals", "Worn Sandals"),
            ("pirate-hat", "Pirate Hat"),
        };

        const string HeadBoneName = "bone_Head";
        const string ItemMarker = "_Item";

        /// <summary>
        /// wow.export names a rigid item mesh "&lt;slot&gt;_Item&lt;id&gt;_&lt;n&gt;"; the slot picks the attachment
        /// point, first one the model has. Off-hand tries the shield's point first — an off-hand
        /// weapon would want the left hand, which only matters once there is one.
        /// </summary>
        static readonly (string slot, int[] attachments)[] SlotAttachments =
        {
            ("Head", new[] { M2Attachments.Helmet }),
            ("Shoulder (L)", new[] { M2Attachments.ShoulderLeft }),
            ("Shoulder (R)", new[] { M2Attachments.ShoulderRight }),
            ("Back", new[] { M2Attachments.Back }),
            ("Main-hand", new[] { M2Attachments.HandRight }),
            ("Off-hand", new[] { M2Attachments.Shield, M2Attachments.HandLeft }),
        };

        public static GearLibrary LoadLibrary() => AssetDatabase.LoadAssetAtPath<GearLibrary>(LibraryPath);

        [MenuItem("WoW Sandbox/Harvest Gear")]
        public static void Harvest()
        {
            var baseExport = Export.Load(BaseFolder);
            if (baseExport == null)
            {
                Debug.LogError($"[GearHarvester] No base export at {ExportRoot}/{BaseFolder}. Export the player's model " +
                               "with nothing equipped and rename its folder to \"base\".");
                return;
            }
            if (baseExport.BodyRenderer == null)
            {
                Debug.LogError($"[GearHarvester] Couldn't find the body ({baseExport.Prefix}Geoset0) in the base export.");
                return;
            }

            Directory.CreateDirectory(OutputRoot);
            var basePixels = ReadPng(baseExport.BodyTexturePath, out int width, out int height);
            string baseBodyPath = $"{OutputRoot}/body_base.png";
            File.Copy(baseExport.BodyTexturePath, baseBodyPath, overwrite: true);
            ImportReadable(baseBodyPath);

            var attachments = ReadRawAttachments(out string rawNote);
            var looks = new List<GearLook>();
            var report = new List<string> { rawNote };
            foreach (var (folder, itemName) in Pieces)
            {
                var piece = Export.Load(folder);
                if (piece == null)
                {
                    report.Add($"{itemName}: no export at {ExportRoot}/{folder} — skipped");
                    continue;
                }
                var look = Harvest(baseExport, basePixels, width, height, piece, folder, itemName, attachments, out string summary);
                if (look != null)
                    looks.Add(look);
                report.Add($"{itemName}: {summary}");
            }

            foreach (var folder in AssetDatabase.GetSubFolders(ExportRoot).Select(Path.GetFileName))
            {
                if (folder != BaseFolder && folder != RawFolder && Pieces.All(p => p.folder != folder))
                    report.Add($"{folder}/: not mapped to an item in GearHarvester.Pieces — skipped");
            }

            var library = LoadLibrary();
            if (library == null)
            {
                library = ScriptableObject.CreateInstance<GearLibrary>();
                AssetDatabase.CreateAsset(library, LibraryPath);
            }
            library.baseBody = AssetDatabase.LoadAssetAtPath<Texture2D>(baseBodyPath);
            library.bodyMaterialName = baseExport.BodyMaterial.name;
            library.bodyTextureProperty = baseExport.BodyTextureProperty;
            library.piecePrefix = baseExport.Prefix;
            library.looks = looks.ToArray();
            EditorUtility.SetDirty(library);
            AssetDatabase.SaveAssets();

            int dressed = 0;
            foreach (var player in Object.FindObjectsByType<Inventory>())
            {
                // Not ??: in the editor a missing component comes back as a fake null object.
                var visuals = player.GetComponent<EquipmentVisuals>();
                if (visuals == null)
                    visuals = Undo.AddComponent<EquipmentVisuals>(player.gameObject);
                Undo.RecordObject(visuals, "Harvest Gear");
                visuals.library = library;
                dressed++;
            }

            Debug.Log($"[GearHarvester] Built {looks.Count} look(s) into {LibraryPath}" +
                      (dressed > 0 ? $" and gave it to the player — save the scene." : " (no player in the scene yet).") +
                      "\n  " + string.Join("\n  ", report), library);
        }

        static GearLook Harvest(Export baseExport, Color32[] basePixels, int width, int height,
            Export piece, string folder, string itemName, Dictionary<int, M2Attachments.Attachment> attachments,
            out string summary)
        {
            var parts = new List<string>();
            string lookPath = $"{OutputRoot}/{folder}.asset";
            var look = AssetDatabase.LoadAssetAtPath<GearLook>(lookPath);
            if (look == null)
            {
                look = ScriptableObject.CreateInstance<GearLook>();
                AssetDatabase.CreateAsset(look, lookPath);
            }
            look.itemName = itemName;

            // --- Painted on ------------------------------------------------------------------
            look.textureLayer = null;
            look.textureRegion = default;
            var pixels = ReadPng(piece.BodyTexturePath, out int pieceWidth, out int pieceHeight);
            if (pieceWidth != width || pieceHeight != height)
            {
                parts.Add($"body texture is {pieceWidth}×{pieceHeight}, base is {width}×{height} — not the same model? texture skipped");
            }
            else if (DiffRegion(basePixels, pixels, width, height, out var region, out int changed))
            {
                var layer = new Color32[region.width * region.height];
                for (int y = 0; y < region.height; y++)
                {
                    for (int x = 0; x < region.width; x++)
                    {
                        int i = (region.y + y) * width + region.x + x;
                        var p = pixels[i];
                        bool differs = !Same(p, basePixels[i]);
                        layer[y * region.width + x] = new Color32(p.r, p.g, p.b, differs ? (byte)255 : (byte)0);
                    }
                }

                string layerPath = $"{OutputRoot}/{folder}_layer.png";
                var texture = new Texture2D(region.width, region.height, TextureFormat.RGBA32, false);
                texture.SetPixels32(layer);
                File.WriteAllBytes(layerPath, texture.EncodeToPNG());
                Object.DestroyImmediate(texture);
                ImportReadable(layerPath);

                look.textureLayer = AssetDatabase.LoadAssetAtPath<Texture2D>(layerPath);
                look.textureRegion = region;
                parts.Add($"{changed:N0} body texture pixels");
            }

            // --- Body pieces -----------------------------------------------------------------
            var basePieces = baseExport.BodyPieces.Keys.ToHashSet();
            var piecePieces = piece.BodyPieces.Keys.ToHashSet();
            look.addsPieces = piecePieces.Except(basePieces).OrderBy(n => n).ToArray();
            look.hidesPieces = basePieces.Except(piecePieces).OrderBy(n => n).ToArray();
            if (look.addsPieces.Length > 0)
                parts.Add("adds " + string.Join(", ", look.addsPieces));
            if (look.hidesPieces.Length > 0)
                parts.Add("hides " + string.Join(", ", look.hidesPieces));

            // --- Separate meshes -------------------------------------------------------------
            var skinned = new List<SkinnedPiece>();
            foreach (var name in look.addsPieces)
            {
                if (piece.BodyPieces[name] is SkinnedMeshRenderer renderer)
                    skinned.Add(Skinned(name, renderer));
            }

            var attached = new List<AttachedPiece>();
            var transforms = piece.Root.GetComponentsInChildren<Transform>(true);
            var head = transforms.FirstOrDefault(t => t.name == HeadBoneName);
            // Attachment bones are indices into the skin's joints, which is the body's bone order.
            var skeleton = piece.BodyRenderer is SkinnedMeshRenderer body ? body.bones : null;
            foreach (var renderer in piece.Root.GetComponentsInChildren<Renderer>(true))
            {
                string name = renderer.gameObject.name;
                int marker = name.IndexOf(ItemMarker, System.StringComparison.Ordinal);
                if (marker < 0)
                    continue;

                if (renderer is SkinnedMeshRenderer itemSkin)
                {
                    skinned.Add(Skinned(name, itemSkin));
                    parts.Add($"skinned mesh {name}");
                    continue;
                }
                if (renderer.GetComponent<MeshFilter>() is not { sharedMesh: not null } filter)
                    continue;

                string slot = name.Substring(0, marker);
                var item = renderer.transform;
                Transform bone = null;
                Vector3 position = default, scale = Vector3.one;
                var rotation = Quaternion.identity;
                string how = null;

                var ids = SlotAttachments.FirstOrDefault(entry => entry.slot == slot).attachments;
                foreach (int id in ids ?? System.Array.Empty<int>())
                {
                    if (!attachments.TryGetValue(id, out var point) || skeleton == null ||
                        point.bone >= skeleton.Length || skeleton[point.bone] == null)
                        continue;
                    // The mesh moved by the attachment's offset, in its own (model) space. glTFast
                    // turns glTF's right-handed axes into Unity's by mirroring X.
                    bone = skeleton[point.bone];
                    var offset = new Vector3(-point.gltfPosition.x, point.gltfPosition.y, point.gltfPosition.z);
                    position = bone.InverseTransformPoint(item.TransformPoint(offset));
                    rotation = Quaternion.Inverse(bone.rotation) * item.rotation;
                    scale = Divide(item.lossyScale, bone.lossyScale);
                    how = $"on attachment {id} ({bone.name})";
                    break;
                }

                if (bone == null && slot == "Head" && head != null &&
                    HelmFitter.TryFitPose(piece.Root, head, new[] { item }, usedVerticesOnly: true,
                        out position, out rotation, out scale))
                {
                    bone = head;
                    how = "on the head, by guess (no raw export for the real attachment point)";
                }

                if (bone == null)
                {
                    parts.Add($"{name}: no attachment point for \"{slot}\" — skipped" +
                              (attachments.Count == 0 ? " (needs the raw export)" : ""));
                    continue;
                }

                attached.Add(new AttachedPiece
                {
                    name = name, mesh = filter.sharedMesh, materials = renderer.sharedMaterials,
                    bone = bone.name, localPosition = position, localRotation = rotation, localScale = scale,
                });
                parts.Add($"{name} {how}");
            }
            look.skinnedPieces = skinned.ToArray();
            look.attachedPieces = attached.ToArray();

            EditorUtility.SetDirty(look);
            summary = parts.Count > 0 ? string.Join("; ", parts) : "no difference from the base — check the export";
            return look;
        }

        /// <summary>The raw folder's .m2 and .skel attachment points; empty if there's no raw export.</summary>
        static Dictionary<int, M2Attachments.Attachment> ReadRawAttachments(out string note)
        {
            string dir = $"{ExportRoot}/{RawFolder}";
            string Find(string pattern) => Directory.Exists(dir)
                ? Directory.GetFiles(dir, pattern, SearchOption.AllDirectories).FirstOrDefault()
                : null;
            string m2 = Find("*.m2"), skel = Find("*.skel");

            var points = M2Attachments.Read(m2, skel);
            if (points.Count > 0)
                note = $"attachment points: {points.Count}, from " +
                       string.Join(" + ", new[] { m2, skel }.Where(p => p != null).Select(Path.GetFileName));
            else if (m2 == null)
                note = $"attachment points: none — no raw export in {dir}, so helms are placed by guess and weapons/shields/shoulders skipped";
            else
                note = $"attachment points: none found in {Path.GetFileName(m2)}" +
                       (skel == null ? " and no .skel next to it" : $" or {Path.GetFileName(skel)}");
            return points;
        }

        static Vector3 Divide(Vector3 a, Vector3 b) =>
            new(a.x / Mathf.Max(b.x, 1e-5f), a.y / Mathf.Max(b.y, 1e-5f), a.z / Mathf.Max(b.z, 1e-5f));

        static SkinnedPiece Skinned(string name, SkinnedMeshRenderer renderer) => new()
        {
            name = name,
            mesh = renderer.sharedMesh,
            materials = renderer.sharedMaterials,
            boneNames = renderer.bones.Select(b => b != null ? b.name : "").ToArray(),
            rootBone = renderer.rootBone != null ? renderer.rootBone.name : null,
        };

        static bool Same(Color32 a, Color32 b) => a.r == b.r && a.g == b.g && a.b == b.b && a.a == b.a;

        /// <summary>The bounding box of every pixel that differs, in Unity's bottom-up pixel rows.</summary>
        static bool DiffRegion(Color32[] a, Color32[] b, int width, int height, out RectInt region, out int changed)
        {
            int minX = width, minY = height, maxX = -1, maxY = -1;
            changed = 0;
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int i = y * width + x;
                    if (Same(a[i], b[i]))
                        continue;
                    changed++;
                    if (x < minX) minX = x;
                    if (x > maxX) maxX = x;
                    if (y < minY) minY = y;
                    if (y > maxY) maxY = y;
                }
            }
            region = changed > 0 ? new RectInt(minX, minY, maxX - minX + 1, maxY - minY + 1) : default;
            return changed > 0;
        }

        /// <summary>Straight from the PNG file, so it needn't be imported readable.</summary>
        static Color32[] ReadPng(string path, out int width, out int height)
        {
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            texture.LoadImage(File.ReadAllBytes(path));
            width = texture.width;
            height = texture.height;
            var pixels = texture.GetPixels32();
            Object.DestroyImmediate(texture);
            return pixels;
        }

        /// <summary>Readable at runtime, exact (no compression, no mips), for EquipmentVisuals to paint from.</summary>
        static void ImportReadable(string path)
        {
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Default;
            importer.isReadable = true;
            importer.mipmapEnabled = false;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.alphaIsTransparency = false;
            importer.sRGBTexture = true;
            importer.maxTextureSize = 8192;
            importer.SaveAndReimport();
        }

        /// <summary>One export folder: its model, body pieces and body texture.</summary>
        class Export
        {
            public GameObject Root;
            public string Prefix;
            public Dictionary<string, Renderer> BodyPieces;
            public Renderer BodyRenderer;
            public Material BodyMaterial;
            public string BodyTextureProperty;
            public string BodyTexturePath;

            public static Export Load(string folder)
            {
                string dir = $"{ExportRoot}/{folder}";
                if (!AssetDatabase.IsValidFolder(dir))
                    return null;
                string gltf = AssetDatabase.FindAssets("t:GameObject", new[] { dir })
                                           .Select(AssetDatabase.GUIDToAssetPath)
                                           .FirstOrDefault(p => p.EndsWith(".gltf"));
                var root = gltf != null ? AssetDatabase.LoadAssetAtPath<GameObject>(gltf) : null;
                if (root == null)
                    return null;

                var export = new Export { Root = root, Prefix = Path.GetFileNameWithoutExtension(gltf) + "_" };
                export.BodyPieces = root.GetComponentsInChildren<Renderer>(true)
                    .Where(r => r.gameObject.name.StartsWith(export.Prefix))
                    .GroupBy(r => r.gameObject.name.Substring(export.Prefix.Length))
                    .ToDictionary(g => g.Key, g => g.First());
                export.BodyPieces.TryGetValue("Geoset0", out export.BodyRenderer);

                // The body texture is whatever the main body mesh's material shows.
                export.BodyMaterial = export.BodyRenderer != null ? export.BodyRenderer.sharedMaterial : null;
                if (export.BodyMaterial != null)
                {
                    foreach (string property in export.BodyMaterial.GetTexturePropertyNames())
                    {
                        var texture = export.BodyMaterial.GetTexture(property);
                        string path = texture != null ? AssetDatabase.GetAssetPath(texture) : null;
                        if (path == null || !path.EndsWith(".png"))
                            continue;
                        export.BodyTextureProperty = property;
                        export.BodyTexturePath = path;
                        break;
                    }
                }
                if (export.BodyTexturePath == null && export.BodyMaterial != null)
                {
                    // Embedded rather than linked: fall back to the export's first image, wow.export's body.
                    export.BodyTexturePath = $"{dir}/data-1.png";
                    export.BodyTextureProperty = export.BodyMaterial.GetTexturePropertyNames()
                        .FirstOrDefault(p => export.BodyMaterial.GetTexture(p) != null);
                }
                return export;
            }
        }
    }
}
