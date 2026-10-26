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
    /// Items can also come from wow.export's Models tab on their own: an item model is in the
    /// same item space as the copy a character export carries — origin at the attachment point —
    /// so it goes on that point the same way, with no character export. That suits what's held
    /// (weapons, shields, tools); anything worn needs the character export.
    ///
    /// Exports are found by themselves: every folder under character/thinhuman/ and every .gltf
    /// under WowExports/item/ gets a row in GearSources, with a guess at its item from its name.
    /// Re-run after adding exports; it rebuilds the whole library and hands it to the player in
    /// the scene.
    /// </summary>
    public static class GearHarvester
    {
        const string ExportRoot = "Assets/WowExports/character/thinhuman";
        const string BaseFolder = "base";
        /// <summary>wow.export's raw M2 export of the same model (Models tab, Export M2 / WMO (Raw), with .skel files).</summary>
        const string RawFolder = "raw";
        const string OutputRoot = "Assets/WowExports/_Generated/Gear";
        const string LibraryPath = OutputRoot + "/GearLibrary.asset";

        const string ItemModelRoot = "Assets/WowExports/item";
        const string SourcesPath = "Assets/WowExports/GearSources.asset";

        /// <summary>
        /// Exports already decided, by name, ahead of any guessing: models whose names would
        /// guess another item (the second maul is the Warhammer; the raid shield is the Aegis).
        /// </summary>
        static readonly (string export, string item)[] Picks =
        {
            ("hammer_2h_maul_b_03white", "Warhammer"),
            ("shield_2h_outlandraid_d_05", "Aegis of the Dawn"),
            // Two lanterns exported, two lantern items.
            ("misc_1h_lantern_a_01silver", "Hooded Lantern"),
            ("misc_1h_lantern_b_01silver", "Bullseye Lantern"),
        };

        /// <summary>
        /// Guesses at an export's item from words in its name (folder or file, lower case, dashes
        /// and spaces dropped). Every guess that matches must agree, or there's no guess — so
        /// "shirt-pants-sandals" (three items) is left for you to decide. More specific entries
        /// first: a two-handed sword before any sword.
        /// </summary>
        static readonly (string[] words, string item)[] Guesses =
        {
            (new[] { "twig", "fishingpole" }, "Twig Fishing Pole"),
            (new[] { "fishingpole" }, "Fishing Pole"),
            (new[] { "pirate", "hat" }, "Pirate Hat"),
            (new[] { "shirt" }, "Linen Shirt"),
            (new[] { "shorts" }, "Cloth Shorts"),
            (new[] { "pants" }, "Cloth Shorts"),
            (new[] { "sandal" }, "Worn Sandals"),
            (new[] { "knife" }, "Dagger"),
            (new[] { "dagger" }, "Dagger"),
            (new[] { "sword_2h" }, "Greatsword"),
            (new[] { "greatsword" }, "Greatsword"),
            (new[] { "sword_1h_long" }, "Longsword"),
            (new[] { "longsword" }, "Longsword"),
            (new[] { "sword_1h_short" }, "Shortsword"),
            (new[] { "shortsword" }, "Shortsword"),
            (new[] { "sword" }, "Shortsword"),
            (new[] { "axe", "battle" }, "Battleaxe"),
            (new[] { "axe_2h" }, "Greataxe"),
            (new[] { "greataxe" }, "Greataxe"),
            (new[] { "battleaxe" }, "Battleaxe"),
            (new[] { "axe" }, "Handaxe"),
            (new[] { "maul" }, "Maul"),
            (new[] { "hammer_2h" }, "Maul"),
            (new[] { "hammer" }, "Warhammer"),
            (new[] { "mace" }, "Mace"),
            (new[] { "legging" }, "Leather Leggings"),
            (new[] { "leather", "leg" }, "Leather Leggings"),
            (new[] { "legguard" }, "Iron Legguards"),
            (new[] { "greaves" }, "Iron Legguards"),
            (new[] { "iron", "leg" }, "Iron Legguards"),
            (new[] { "plate", "leg" }, "Iron Legguards"),
            (new[] { "spaulder" }, "Leather Spaulders"),
            (new[] { "leather", "shoulder" }, "Leather Spaulders"),
            (new[] { "pauldron" }, "Iron Pauldrons"),
            (new[] { "iron", "shoulder" }, "Iron Pauldrons"),
            (new[] { "plate", "shoulder" }, "Iron Pauldrons"),
            (new[] { "torch" }, "Torch"),
            (new[] { "lantern" }, "Hooded Lantern"),
            (new[] { "shield" }, "Shield"),
            (new[] { "buckler" }, "Shield"),
        };

        const string HeadBoneName = "bone_Head";
        const string ItemMarker = "_Item";

        /// <summary>
        /// wow.export names a rigid item mesh "&lt;slot&gt;_Item&lt;id&gt;_&lt;n&gt;"; the slot picks the attachment
        /// point, first one the model has. Off-hand tries the shield's point first (on the forearm);
        /// something held in the off hand — a torch, a lantern — goes in the left hand itself.
        /// </summary>
        static readonly (string slot, int[] attachments)[] SlotAttachments =
        {
            ("Head", new[] { M2Attachments.Helmet }),
            ("Shoulder (L)", new[] { M2Attachments.ShoulderLeft }),
            ("Shoulder (R)", new[] { M2Attachments.ShoulderRight }),
            ("Back", new[] { M2Attachments.Back }),
            ("Main-hand", new[] { M2Attachments.HandRight }),
            ("Off-hand", new[] { M2Attachments.Shield, M2Attachments.HandLeft }),
            ("Off-hand (held)", new[] { M2Attachments.HandLeft, M2Attachments.Shield }),
        };

        /// <summary>
        /// Built-in fits for items whose WoW size or placement looks off on this character, so
        /// they don't depend on the (gitignored, per-machine) GearSources asset. A row's own
        /// Scale or Offset overrides these. Offset is in the character's axes (y up).
        /// </summary>
        static readonly Dictionary<string, (float scale, Vector3 offset)> Fits = new()
        {
            // WoW's plate shoulders are huge on a human.
            ["Iron Pauldrons"] = (0.65f, Vector3.zero),
            // Sits a little high on the head.
            ["Iron Helm"] = (1f, new Vector3(0f, -0.03f, 0f)),
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
            var report = new List<string> { rawNote };
            var sources = LoadSources(report);

            // One look per item. A second export of the same item is left out, unless it's an
            // item model adding a slot the look doesn't have yet (a left and a right shoulder).
            var looks = new List<GearLook>();
            var byItem = new Dictionary<string, (GearLook look, HashSet<string> slots, string from)>();
            foreach (var source in sources.sources)
            {
                if (string.IsNullOrWhiteSpace(source.item))
                    continue;
                string label = $"{source.item} ← {Path.GetFileName(source.export)}";
                if (!ItemCatalog.TryFind(source.item, out var catalogItem))
                {
                    report.Add($"{label}: no item called \"{source.item}\" in the catalog — fix it in GearSources. " +
                               $"Close: {string.Join(", ", Closest(source.item))}");
                    continue;
                }

                bool standalone = source.export.EndsWith(".gltf");
                byItem.TryGetValue(source.item, out var existing);
                if (!standalone)
                {
                    if (existing.look != null)
                    {
                        report.Add($"{label}: {source.item} already has a look from {existing.from} — skipped");
                        continue;
                    }
                    var piece = Export.Load(source.export);
                    if (piece == null)
                    {
                        report.Add($"{label}: no export at {ExportRoot}/{source.export} — skipped");
                        continue;
                    }
                    var look = Harvest(baseExport, basePixels, width, height, piece, source.export, source.item, attachments, out string summary);
                    ApplyFit(look, look.attachedPieces, source, baseExport, ref summary);
                    looks.Add(look);
                    byItem[source.item] = (look, new HashSet<string> { "*" }, source.export);
                    report.Add($"{label}: {summary}");
                    continue;
                }

                string slot = !string.IsNullOrWhiteSpace(source.slot) ? source.slot.Trim() : HeldSlot(catalogItem, source.export);
                if (slot == null)
                {
                    report.Add($"{label}: worn items need a character export (the model wearing it), not the item model — skipped");
                    continue;
                }
                if (existing.look != null && (existing.slots.Contains("*") || existing.slots.Contains(slot)))
                {
                    report.Add($"{label}: {source.item} already has a look from {existing.from} — skipped");
                    continue;
                }

                var pieces = StandalonePieces(baseExport, attachments, source.export, slot, out string how);
                if (pieces == null)
                {
                    report.Add($"{label}: {how}");
                    continue;
                }
                if (existing.look != null)
                {
                    OffsetPieces(pieces, Fit(source).offset, baseExport);
                    existing.look.attachedPieces = existing.look.attachedPieces.Concat(pieces).ToArray();
                    existing.slots.Add(slot);
                    EditorUtility.SetDirty(existing.look);
                }
                else
                {
                    var look = StandaloneLook(source.export, source.item, pieces);
                    ApplyFit(look, look.attachedPieces, source, baseExport, ref how);
                    looks.Add(look);
                    byItem[source.item] = (look, new HashSet<string> { slot }, Path.GetFileName(source.export));
                }
                report.Add($"{label}: {how}");
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
            library.sheathPoints = SheathPoints(baseExport, attachments, report);
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
                      $" Which export dresses which item: {SourcesPath} (click this message to select it)." +
                      "\n  " + string.Join("\n  ", report), sources);
        }

        /// <summary>
        /// GearSources, with a row added for every export it doesn't have yet: character folders
        /// first, then item models, each with a guess at its item.
        /// </summary>
        static GearSources LoadSources(List<string> report)
        {
            var sources = AssetDatabase.LoadAssetAtPath<GearSources>(SourcesPath);
            if (sources == null)
            {
                sources = ScriptableObject.CreateInstance<GearSources>();
                AssetDatabase.CreateAsset(sources, SourcesPath);
            }

            var folders = AssetDatabase.GetSubFolders(ExportRoot).Select(Path.GetFileName)
                                       .Where(f => f != BaseFolder && f != RawFolder).OrderBy(f => f);
            var models = Directory.Exists(ItemModelRoot)
                ? Directory.GetFiles(ItemModelRoot, "*.gltf", SearchOption.AllDirectories)
                           .Select(p => p.Replace('\\', '/')).OrderBy(p => p)
                : Enumerable.Empty<string>();

            var known = new HashSet<string>(sources.sources.Select(s => s.export));
            foreach (string export in folders.Concat(models))
            {
                if (!known.Add(export))
                    continue;
                string guess = Guess(Path.GetFileNameWithoutExtension(export));
                sources.sources.Add(new GearSources.Source { export = export, item = guess ?? "", slot = "" });
                report.Add(guess != null
                    ? $"new export {Path.GetFileName(export)} → {guess} (a guess — change it in GearSources if wrong)"
                    : $"new export {Path.GetFileName(export)}: couldn't tell which item it is — set it in GearSources");
            }

            foreach (var source in sources.sources)
            {
                bool there = source.export.EndsWith(".gltf") ? File.Exists(source.export) : AssetDatabase.IsValidFolder($"{ExportRoot}/{source.export}");
                if (!there)
                    report.Add($"{Path.GetFileName(source.export)}: export not found any more — remove its row from GearSources");
            }

            EditorUtility.SetDirty(sources);
            return sources;
        }

        static string Guess(string name)
        {
            string picked = Picks.FirstOrDefault(p => p.export == name).item;
            if (picked != null)
                return picked;

            string key = name.ToLowerInvariant().Replace("-", "").Replace(" ", "");
            // The catalog's own name, written any way ("long-sword", "Warhammer").
            string exact = ItemCatalog.Names.FirstOrDefault(n => n.ToLowerInvariant().Replace(" ", "").Replace("'", "") == key);
            if (exact != null)
                return exact;
            // A catalog name inside a longer one ("studded-leather-armor"): the longest wins, so
            // that's Studded Leather rather than Leather Armor.
            string within = ItemCatalog.Names
                .Select(n => (name: n, key: n.ToLowerInvariant().Replace(" ", "").Replace("'", "")))
                .Where(n => n.key.Length >= 4 && key.Contains(n.key))
                .OrderByDescending(n => n.key.Length)
                .Select(n => n.name).FirstOrDefault();
            if (within != null)
                return within;

            var matches = Guesses.Where(g => g.words.All(key.Contains)).Select(g => g.item).ToList();
            if (matches.Count == 0)
                return null;
            // Specific beats generic only within one item's family: "sword_1h_long" also contains
            // "sword", so the first match wins unless a match from a different family disagrees.
            string first = matches[0];
            bool related = matches.All(m => m == first || Family(m) == Family(first));
            return related ? first : null;
        }

        // A long sword's name also says "sword", a twig pole's "fishingpole": a refinement within
        // one family, not a disagreement.
        static string Family(string item) =>
            item.Contains("sword") ? "sword" : item.Contains("axe") ? "axe" : item.EndsWith("Fishing Pole") ? "pole" :
            item is "Maul" or "Warhammer" ? "hammer" : item;

        static IEnumerable<string> Closest(string name)
        {
            string key = name.ToLowerInvariant();
            return ItemCatalog.Names
                .OrderBy(n => n.ToLowerInvariant().Contains(key) || key.Contains(n.ToLowerInvariant()) ? 0 : 1)
                .ThenBy(n => Mathf.Abs(n.Length - name.Length))
                .Take(4);
        }

        /// <summary>
        /// Where an item model is held, from the catalog item: weapons and tools in the main hand,
        /// shields on the off arm, torches and lanterns in the off hand, helms on the head, cloaks on the back, shoulders by the file's
        /// own lshoulder/rshoulder. Null for worn items, which need a character export.
        /// </summary>
        static string HeldSlot(Item item, string path)
        {
            if (item.kind == ItemKind.Tool)
                return "Main-hand";
            if (item.kind == ItemKind.Light)
                return "Off-hand (held)";
            string file = Path.GetFileName(path).ToLowerInvariant();
            return item.slot switch
            {
                EquipSlot.MainHand => "Main-hand",
                EquipSlot.OffHand => "Off-hand",
                EquipSlot.Head => "Head",
                EquipSlot.Back => "Back",
                EquipSlot.Shoulders => file.StartsWith("r") ? "Shoulder (R)" : "Shoulder (L)",
                _ => null,
            };
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
                TryAttach(slot, item, skeleton, attachments,
                    out var bone, out var position, out var rotation, out var scale, out string how);

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

        /// <summary>
        /// The pose, relative to its attachment bone, of an item mesh whose own space is
        /// <paramref name="space"/> (the character model's space, at rest): the mesh moved by the
        /// attachment's offset. Null bone if the slot has no attachment point the model has.
        /// </summary>
        /// <summary>The row's scale onto the look's attached meshes (the first row for an item sets it).</summary>
        /// <summary>The row's Scale and Offset where set, else the item's built-in fit, else none.</summary>
        static (float scale, Vector3 offset) Fit(GearSources.Source source)
        {
            Fits.TryGetValue(source.item ?? "", out var fit);
            float scale = source.scale > 0f && !Mathf.Approximately(source.scale, 1f) ? source.scale
                        : fit.scale > 0f ? fit.scale : 1f;
            var offset = source.offset != Vector3.zero ? source.offset : fit.offset;
            return (scale, offset);
        }

        /// <summary>The item's fit onto its look: the scale for its attached meshes, and their offset.</summary>
        static void ApplyFit(GearLook look, AttachedPiece[] pieces, GearSources.Source source, Export baseExport, ref string summary)
        {
            var (scale, offset) = Fit(source);
            look.attachedScale = scale;
            OffsetPieces(pieces, offset, baseExport);
            EditorUtility.SetDirty(look);
            if (!Mathf.Approximately(scale, 1f))
                summary += $"; attached meshes at {scale:0.##}× size";
            if (offset != Vector3.zero && pieces.Length > 0)
                summary += $"; moved by {offset}";
        }

        /// <summary>
        /// Moves each piece by <paramref name="offset"/> in the character's axes, turned into its
        /// bone's own space at rest, so the move rides along with the bone as it animates.
        /// </summary>
        static void OffsetPieces(IEnumerable<AttachedPiece> pieces, Vector3 offset, Export baseExport)
        {
            if (offset == Vector3.zero)
                return;
            var root = baseExport.Root.transform;
            var bones = root.GetComponentsInChildren<Transform>(true);
            foreach (var piece in pieces)
            {
                var bone = bones.FirstOrDefault(t => t.name == piece.bone);
                if (bone != null)
                    piece.localPosition += bone.InverseTransformVector(root.TransformVector(offset));
            }
        }

        static void TryAttach(string slot, Transform space, Transform[] skeleton, Dictionary<int, M2Attachments.Attachment> attachments,
            out Transform bone, out Vector3 position, out Quaternion rotation, out Vector3 scale, out string how) =>
            TryAttach(SlotAttachments.FirstOrDefault(entry => entry.slot == slot).attachments, space, skeleton, attachments,
                out bone, out position, out rotation, out scale, out how);

        /// <summary>TryAttach on the first of <paramref name="ids"/> the model has.</summary>
        static void TryAttach(int[] ids, Transform space, Transform[] skeleton, Dictionary<int, M2Attachments.Attachment> attachments,
            out Transform bone, out Vector3 position, out Quaternion rotation, out Vector3 scale, out string how)
        {
            bone = null;
            position = default;
            rotation = Quaternion.identity;
            scale = Vector3.one;
            how = null;

            foreach (int id in ids ?? System.Array.Empty<int>())
            {
                if (!attachments.TryGetValue(id, out var point) || skeleton == null ||
                    point.bone >= skeleton.Length || skeleton[point.bone] == null)
                    continue;
                // glTFast turns glTF's right-handed axes into Unity's by mirroring X.
                bone = skeleton[point.bone];
                var offset = new Vector3(-point.gltfPosition.x, point.gltfPosition.y, point.gltfPosition.z);
                position = bone.InverseTransformPoint(space.TransformPoint(offset));
                rotation = Quaternion.Inverse(bone.rotation) * space.rotation;
                scale = Divide(space.lossyScale, bone.lossyScale);
                how = $"on attachment {id} ({bone.name})";
                return;
            }
        }

        static readonly (SheathSpot spot, int id)[] SheathAttachments =
        {
            (SheathSpot.Back, M2Attachments.SheathMainHand),
            (SheathSpot.Shield, M2Attachments.SheathShield),
            (SheathSpot.Hip, M2Attachments.HipWeaponLeft),
        };

        /// <summary>
        /// The model's sheath points, posed like StandalonePieces poses an item model: in the
        /// body's space, which is where every item mesh's own space sits.
        /// </summary>
        static SheathPoint[] SheathPoints(Export baseExport, Dictionary<int, M2Attachments.Attachment> attachments, List<string> report)
        {
            var space = baseExport.BodyRenderer.transform;
            var skeleton = baseExport.BodyRenderer is SkinnedMeshRenderer body ? body.bones : null;
            var points = new List<SheathPoint>();
            foreach (var (spot, id) in SheathAttachments)
            {
                TryAttach(new[] { id }, space, skeleton, attachments,
                    out var bone, out var position, out var rotation, out var scale, out string how);
                if (bone == null)
                {
                    report.Add($"sheath {spot}: no attachment {id} on the model — weapons put away there stay in hand" +
                               (attachments.Count == 0 ? " (needs the raw export)" : ""));
                    continue;
                }
                points.Add(new SheathPoint
                {
                    spot = spot, bone = bone.name, localPosition = position, localRotation = rotation, localScale = scale,
                });
                report.Add($"sheath {spot}: {how}");
            }
            return points.ToArray();
        }

        /// <summary>
        /// An item model's meshes on the attachment point for <paramref name="slot"/>, or null (with
        /// why, in <paramref name="how"/>). They're skinned to the item's own little skeleton (a
        /// fishing line, a dagger's tassel), but drawn rigid in their rest pose.
        /// </summary>
        static List<AttachedPiece> StandalonePieces(Export baseExport, Dictionary<int, M2Attachments.Attachment> attachments,
            string path, string slot, out string how)
        {
            var root = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (root == null)
            {
                how = $"no item model at {path} — skipped";
                return null;
            }

            // The body mesh's node sits at the model's origin, so its space is the model's: where
            // a character export's own copy of the item would be.
            var space = baseExport.BodyRenderer.transform;
            var skeleton = baseExport.BodyRenderer is SkinnedMeshRenderer body ? body.bones : null;
            TryAttach(slot, space, skeleton, attachments,
                out var bone, out var position, out var rotation, out var scale, out how);
            if (bone == null)
            {
                how = $"no attachment point for \"{slot}\" — skipped" + (attachments.Count == 0 ? " (needs the raw export)" : "");
                return null;
            }

            var pieces = new List<AttachedPiece>();
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                var mesh = renderer is SkinnedMeshRenderer skin ? skin.sharedMesh
                    : renderer.GetComponent<MeshFilter>() is { } filter ? filter.sharedMesh : null;
                if (mesh == null)
                    continue;
                pieces.Add(new AttachedPiece
                {
                    name = renderer.gameObject.name, mesh = mesh, materials = renderer.sharedMaterials,
                    bone = bone.name, localPosition = position, localRotation = rotation, localScale = scale,
                });
            }
            if (pieces.Count == 0)
            {
                how = $"no meshes in {path} — skipped";
                return null;
            }
            how = $"{Path.GetFileName(path)} {how}";
            return pieces;
        }

        static GearLook StandaloneLook(string path, string itemName, List<AttachedPiece> pieces)
        {
            string lookPath = $"{OutputRoot}/{Path.GetFileNameWithoutExtension(path)}.asset";
            var look = AssetDatabase.LoadAssetAtPath<GearLook>(lookPath);
            if (look == null)
            {
                look = ScriptableObject.CreateInstance<GearLook>();
                AssetDatabase.CreateAsset(look, lookPath);
            }
            look.itemName = itemName;
            look.textureLayer = null;
            look.textureRegion = default;
            look.addsPieces = System.Array.Empty<string>();
            look.hidesPieces = System.Array.Empty<string>();
            look.skinnedPieces = System.Array.Empty<SkinnedPiece>();
            look.attachedPieces = pieces.ToArray();
            EditorUtility.SetDirty(look);
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
