using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace WowSandbox.EditorTools
{
    /// <summary>
    /// Places interactables: treasure chests (the exported TreasureTracker, swinging open on
    /// its own Open clip, its golden light shaft made to glow) and items lying on the ground
    /// (a model per kind of item, picked in the window). Both land where the Scene view is
    /// looking — on whatever's under its centre — and are selected so you can move them and
    /// set what they hold in the Inspector.
    /// </summary>
    public class InteractableSetup : EditorWindow
    {
        /// <summary>Where wow.export puts WoW's chests (world/skillactivated/containers), and one more.</summary>
        const string ChestFolder = "Assets/WowExports/world/skillactivated/containers";
        const string ExtraChest = "Assets/WowExports/creatures/TreasureTracker.gltf";
        internal const string OutputDir = "Assets/WowExports/_Generated/Interactables";

        /// <summary>Every exported chest model, the containers folder first.</summary>
        internal static string[] ChestModels()
        {
            var found = Directory.Exists(ChestFolder)
                ? Directory.GetFiles(ChestFolder, "*.gltf", SearchOption.AllDirectories).Select(p => p.Replace('\\', '/')).OrderBy(p => p).ToList()
                : new System.Collections.Generic.List<string>();
            if (File.Exists(ExtraChest))
                found.Add(ExtraChest);
            return found.ToArray();
        }

        // --- Treasure chest ------------------------------------------------------------------

        /// <summary>
        /// Puts the chest model at <paramref name="modelPath"/> where the Scene view is looking,
        /// <paramref name="height"/> tall, ready to open and loot.
        /// </summary>
        internal static void PlaceChest(string modelPath, float height)
        {
            if (!Ground(out var point, out var facing))
                return;
            var chest = BuildChest(modelPath, height);
            if (chest == null)
                return;
            Undo.RegisterCreatedObjectUndo(chest, "Place Treasure Chest");
            chest.transform.SetPositionAndRotation(point, facing);

            Selection.activeGameObject = chest;
            EditorSceneManager.MarkSceneDirty(chest.scene);
            Debug.Log("[Interactables] Placed a treasure chest (a potion, 5 gold and one random treasure to start). Set " +
                      "its contents and Bonus Treasure in the Inspector, then save the scene.", chest);
        }

        /// <summary>
        /// A chest from the model at <paramref name="modelPath"/>, <paramref name="height"/> tall,
        /// at the origin: animated, its glows and coin layers fixed, solid, and holding a potion,
        /// 5 gold and one random treasure. Null without the model.
        /// </summary>
        internal static GameObject BuildChest(string modelPath, float height)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            if (prefab == null)
            {
                Debug.LogWarning($"[Interactables] No chest model at {modelPath} — export one (world/skillactivated/containers) as glTF.");
                return null;
            }

            var chest = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            chest.name = "Treasure Chest";

            var animator = chest.GetComponentInChildren<Animator>();
            if (animator == null)
                animator = chest.AddComponent<Animator>();
            animator.runtimeAnimatorController = ChestController(modelPath);
            animator.applyRootMotion = false;

            Glow(chest);
            Cutouts(chest);
            // Sized by height, measured without any glow.
            var bounds = RendererBounds(chest, solidOnly: true);
            if (height > 0f && bounds.size.y > 0.01f)
                chest.transform.localScale = Vector3.one * (height / bounds.size.y);
            AddBoxCollider(chest, trigger: false, minSize: 0.3f);
            chest.AddComponent<LootDrop>().table = LootTable.None;
            var treasure = chest.AddComponent<TreasureChest>();
            treasure.displayName = "Treasure Chest";
            treasure.contents.Add(new TreasureChest.Content { item = "Potion of Healing", count = 1 });
            treasure.copper = 5 * 10000;
            treasure.bonusTreasure = 1;
            return chest;
        }

        /// <summary>Closed until Open (trigger), then the lid swings up and stays up.</summary>
        static AnimatorController ChestController(string modelPath)
        {
            Directory.CreateDirectory(OutputDir);
            string model = Path.GetFileNameWithoutExtension(modelPath);
            string path = $"{OutputDir}/{model}.controller";
            var existing = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
            if (existing != null)
                return existing;

            var clips = AssetDatabase.LoadAllAssetRepresentationsAtPath(modelPath).OfType<AnimationClip>().ToArray();
            AnimationClip Clip(string prefix, bool loop)
            {
                var original = clips.FirstOrDefault(c => c.name.StartsWith(prefix + " (") || c.name == prefix);
                if (original == null)
                    return null;
                var copy = Object.Instantiate(original);
                copy.name = prefix;
                var settings = AnimationUtility.GetAnimationClipSettings(copy);
                settings.loopTime = loop;
                AnimationUtility.SetAnimationClipSettings(copy, settings);
                string clipPath = $"{OutputDir}/{model}_{prefix}.anim";
                AssetDatabase.DeleteAsset(clipPath);
                AssetDatabase.CreateAsset(copy, clipPath);
                return copy;
            }

            var closed = Clip("Stand", loop: true) ?? Clip("Closed", loop: true);
            var open = Clip("Open", loop: false);
            var opened = Clip("Opened", loop: true);

            var controller = AnimatorController.CreateAnimatorControllerAtPath(path);
            controller.AddParameter("Open", AnimatorControllerParameterType.Trigger);
            var machine = controller.layers[0].stateMachine;
            var closedState = machine.AddState("Closed");
            closedState.motion = closed;
            machine.defaultState = closedState;
            if (open != null)
            {
                var openState = machine.AddState("Open");
                openState.motion = open;
                var toOpen = closedState.AddTransition(openState);
                toOpen.AddCondition(AnimatorConditionMode.If, 0f, "Open");
                toOpen.hasExitTime = false;
                toOpen.duration = 0.05f;
                if (opened != null)
                {
                    var openedState = machine.AddState("Opened");
                    openedState.motion = opened;
                    var stay = openState.AddTransition(openedState);
                    stay.hasExitTime = true;
                    stay.exitTime = 0.98f;
                    stay.duration = 0.02f;
                }
            }
            else
            {
                Debug.LogWarning("[Interactables] The chest model has no Open clip — it won't animate.");
            }
            AssetDatabase.SaveAssets();
            return controller;
        }

        /// <summary>
        /// WoW's light shafts and glows (the chest's golden beam) import as opaque sheets: give
        /// them an additive, unlit, two-sided copy of their texture so they glow instead.
        /// </summary>
        internal static void Glow(GameObject root)
        {
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                var materials = renderer.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < materials.Length; i++)
                {
                    var source = materials[i];
                    if (source == null)
                        continue;
                    string name = source.name.ToLowerInvariant();
                    if (!(name.Contains("lightshaft") || name.Contains("glow") || name.Contains("beam") || name.Contains("shine") ||
                          name.Contains("smoke") || name.Contains("cloud") || name.Contains("wispy")))
                        continue;
                    materials[i] = GlowMaterial(source);
                    changed = true;
                }
                if (changed)
                    renderer.sharedMaterials = materials;
                renderer.shadowCastingMode = changed ? UnityEngine.Rendering.ShadowCastingMode.Off : renderer.shadowCastingMode;
            }
        }

        /// <summary>
        /// Layers drawn over a model — a chest's pile of gold coins — whose texture is mostly
        /// see-through: wow.export doesn't mark them, so they'd cover the chest as opaque sheets.
        /// They get a cut-out copy. A texture only slightly see-through (a shine mask) stays solid.
        /// </summary>
        internal static void Cutouts(GameObject root)
        {
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                var materials = renderer.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < materials.Length; i++)
                {
                    var source = materials[i];
                    if (source == null || source.name.EndsWith("_Glow") || source.name.EndsWith("_Cutout"))
                        continue;
                    var texture = BaseTexture(source);
                    if (texture == null || SeeThroughFraction(texture) < 0.25f)
                        continue;
                    materials[i] = CutoutMaterial(source, texture);
                    changed = true;
                }
                if (changed)
                    renderer.sharedMaterials = materials;
            }
        }

        static Texture BaseTexture(Material source)
        {
            foreach (string property in new[] { "baseColorTexture", "_BaseMap", "_MainTex" })
                if (source.HasProperty(property) && source.GetTexture(property) != null)
                    return source.GetTexture(property);
            return source.mainTexture;
        }

        static float SeeThroughFraction(Texture texture)
        {
            string path = AssetDatabase.GetAssetPath(texture);
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                return 0f;
            var copy = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            float fraction = 0f;
            if (copy.LoadImage(File.ReadAllBytes(path)))
            {
                var pixels = copy.GetPixels32();
                int clear = 0, seen = 0;
                for (int i = 0; i < pixels.Length; i += 5, seen++)
                    if (pixels[i].a < 128) clear++;
                fraction = seen > 0 ? clear / (float)seen : 0f;
            }
            Object.DestroyImmediate(copy);
            return fraction;
        }

        static Material CutoutMaterial(Material source, Texture texture)
        {
            Directory.CreateDirectory(OutputDir);
            string path = $"{OutputDir}/{source.name}_Cutout.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                material = new Material(shader) { name = source.name + "_Cutout" };
                AssetDatabase.CreateAsset(material, path);
            }
            material.SetTexture("_BaseMap", texture);
            material.mainTexture = texture;
            material.SetColor("_BaseColor", Color.white);
            material.SetFloat("_AlphaClip", 1f);
            material.SetFloat("_Cutoff", 0.5f);
            material.EnableKeyword("_ALPHATEST_ON");
            material.SetFloat("_Cull", 0f);
            material.SetFloat("_Smoothness", 0.3f);
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.AlphaTest;
            EditorUtility.SetDirty(material);
            return material;
        }

        static Material GlowMaterial(Material source)
        {
            Directory.CreateDirectory(OutputDir);
            string path = $"{OutputDir}/{source.name}_Glow.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit") ?? Shader.Find("Sprites/Default");
                material = new Material(shader) { name = source.name + "_Glow" };
                AssetDatabase.CreateAsset(material, path);
            }
            Texture texture = null;
            foreach (string property in new[] { "baseColorTexture", "_BaseMap", "_MainTex" })
                if (source.HasProperty(property) && source.GetTexture(property) != null)
                    texture = source.GetTexture(property);
            texture ??= source.mainTexture;
            material.SetTexture("_BaseMap", texture);
            material.mainTexture = texture;
            material.SetColor("_BaseColor", new Color(1f, 0.9f, 0.6f, 0.8f));
            // Transparent, additive, no depth write, both sides.
            material.SetFloat("_Surface", 1f);
            material.SetFloat("_Blend", 2f);
            material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);
            material.SetFloat("_ZWrite", 0f);
            material.SetFloat("_Cull", 0f);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            EditorUtility.SetDirty(material);
            return material;
        }

        // --- Ground items --------------------------------------------------------------------

        enum PickupKind { Weapon, Armor, Shield, Clothing, Jewelry, Potion, TradeGood, Tool, Coin, Map, Light, Page }

        // The dropdown's entry for the map (MapPickup), which isn't a catalog item.
        const string MapChoice = "<Map>";
        // ...and for a journal page (JournalPagePickup), whose number is picked below it.
        const string PageChoice = "<Journal Page>";

        [SerializeField] string itemName = "Longsword";
        [SerializeField] int count = 1;
        [SerializeField] int copper;
        [SerializeField] float size = 0.8f;
        [SerializeField] int journalPage = 1;
        Vector2 _scroll;

        [MenuItem("WoW Sandbox/Place Ground Item")]
        static void OpenGroundItem()
        {
            var window = GetWindow<InteractableSetup>(true, "Place Ground Item");
            window.minSize = new Vector2(420f, 480f);
        }

        static string ModelKey(PickupKind kind) => "WowSandbox.GroundItemModel." + kind;

        // The Item dropdown: only items with a model to show — their own gear model or one set for
        // their kind. "Group/Name" labels make submenus; "" is coin only. Rebuilt when a kind's
        // model changes or the window regains focus (after a Harvest Gear run).
        static string[] _itemChoiceNames, _itemChoiceLabels;
        static string[] ItemChoiceNames { get { BuildItemChoices(); return _itemChoiceNames; } }
        static string[] ItemChoiceLabels { get { BuildItemChoices(); return _itemChoiceLabels; } }

        void OnFocus() => _itemChoiceNames = null;

        static bool HasKindModel(PickupKind kind) =>
            AssetDatabase.LoadAssetAtPath<GameObject>(EditorPrefs.GetString(ModelKey(kind), "")) != null;

        static bool HasModel(Item item, GearLibrary library)
        {
            var look = library != null ? library.For(item) : null;
            return (look != null && look.attachedPieces.Any(p => p.mesh != null)) || HasKindModel(KindOf(item));
        }

        static void BuildItemChoices()
        {
            if (_itemChoiceNames != null)
                return;
            var library = GearHarvester.LoadLibrary();
            var groups = new (string group, IEnumerable<Item> items)[]
            {
                ("Weapons", ItemCatalog.Weapons),
                ("Armor", ItemCatalog.Armors.Append(ItemCatalog.Shield)),
                ("Clothing", ItemCatalog.Clothing.Concat(ItemCatalog.LootOnly)),
                ("Jewelry", ItemCatalog.Jewelry),
                ("Tools", ItemCatalog.Tools),
                ("Lights", ItemCatalog.Lights),
                ("Potions", ItemCatalog.Potions),
                ("Trade Goods", ItemCatalog.TradeGoods),
                ("Fish", ItemCatalog.Fish.Skip(1)),
                ("Legendaries", ItemCatalog.Legendaries),
            };
            var names = new List<string>();
            var labels = new List<string>();
            if (HasKindModel(PickupKind.Coin))
            {
                names.Add("");
                labels.Add("Coin only");
            }
            if (HasKindModel(PickupKind.Map))
            {
                names.Add(MapChoice);
                labels.Add("Map (unlocks the minimap and M)");
            }
            // Always listed: without a model of its own it borrows the map's, or a paper stand-in.
            names.Add(PageChoice);
            labels.Add("Journal Page (story)");
            // Lights are always listed: without a model of their own they get a stand-in (StandInLight).
            foreach (var (group, items) in groups)
                foreach (var item in items.Where(item => HasModel(item, library) || item.kind == ItemKind.Light))
                {
                    names.Add(item.name);
                    labels.Add(group + "/" + item.name);
                }
            _itemChoiceNames = names.ToArray();
            _itemChoiceLabels = labels.ToArray();
        }

        void OnGUI()
        {
            EditorGUILayout.HelpBox("Places an item on the ground where the Scene view is looking. Click it in Play to " +
                                    "pick it up. Weapons, shields, helms and shoulders use their own gear model (Harvest Gear); " +
                                    "anything else uses the model set for its kind below.", MessageType.Info);
            bool anyChoices = ItemChoiceNames.Length > 0;
            if (anyChoices)
            {
                int chosen = System.Array.IndexOf(ItemChoiceNames, string.IsNullOrWhiteSpace(itemName) ? "" : itemName.Trim());
                if (chosen < 0)
                    chosen = Mathf.Max(0, System.Array.IndexOf(ItemChoiceNames, "Longsword"));
                chosen = EditorGUILayout.Popup(new GUIContent("Item", "Items with a model to show: their own gear model, " +
                                                              "or one set for their kind below."), chosen, ItemChoiceLabels);
                itemName = ItemChoiceNames[chosen];
            }
            else
            {
                EditorGUILayout.HelpBox("No items have a model yet. Run Harvest Gear, or set a model for a kind below.",
                                        MessageType.Warning);
            }
            if (itemName == PageChoice)
            {
                string[] pageLabels = JournalPages.All.Select(p => $"{p.number}. {p.title} ({JournalPages.HandName(p.hand)})").ToArray();
                int pageIndex = Mathf.Max(0, System.Array.FindIndex(JournalPages.All, p => p.number == journalPage));
                pageIndex = EditorGUILayout.Popup(new GUIContent("Page", "Which journal page, as numbered in docs/LORE.md."),
                                                  pageIndex, pageLabels);
                journalPage = JournalPages.All[pageIndex].number;
            }
            count = Mathf.Max(1, EditorGUILayout.IntField("Count", count));
            copper = Mathf.Max(0, EditorGUILayout.IntField(new GUIContent("Coin (copper)", "100 = 1 silver, 10,000 = 1 gold."), copper));
            size = EditorGUILayout.FloatField(new GUIContent("Size", "Its longest side, in world units. 0 keeps the model's own size."), size);

            GUILayout.Space(6f);
            EditorGUILayout.LabelField("Model for each kind of item", EditorStyles.boldLabel);
            _scroll = EditorGUILayout.BeginScrollView(_scroll, GUILayout.Height(System.Enum.GetValues(typeof(PickupKind)).Length * 21f + 6f));
            foreach (PickupKind kind in System.Enum.GetValues(typeof(PickupKind)))
            {
                var current = AssetDatabase.LoadAssetAtPath<GameObject>(EditorPrefs.GetString(ModelKey(kind), ""));
                var picked = (GameObject)EditorGUILayout.ObjectField(kind.ToString(), current, typeof(GameObject), false);
                if (picked != current)
                {
                    EditorPrefs.SetString(ModelKey(kind), picked != null ? AssetDatabase.GetAssetPath(picked) : "");
                    _itemChoiceNames = null;
                }
            }
            EditorGUILayout.EndScrollView();

            using (new EditorGUI.DisabledScope(!anyChoices))
                if (GUILayout.Button("Place", GUILayout.Height(28f)))
                    PlaceGroundItem();
        }

        void PlaceGroundItem()
        {
            bool map = itemName == MapChoice;
            bool page = itemName == PageChoice;
            Item catalogItem = null;
            if (!map && !page && !string.IsNullOrWhiteSpace(itemName) && !ItemCatalog.TryFind(itemName.Trim(), out catalogItem))
            {
                Debug.LogWarning($"[Interactables] No item called \"{itemName}\".");
                return;
            }
            if (!map && !page && catalogItem == null && copper <= 0)
            {
                Debug.LogWarning("[Interactables] Give it an item or some coin.");
                return;
            }
            if (!Ground(out var point, out var facing))
                return;

            var kind = map ? PickupKind.Map : page ? PickupKind.Page : KindOf(catalogItem);
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(EditorPrefs.GetString(ModelKey(kind), ""));
            if (page && model == null)
                model = AssetDatabase.LoadAssetAtPath<GameObject>(EditorPrefs.GetString(ModelKey(PickupKind.Map), ""));
            string label = map ? "Tattered Map" : page ? "Water-stained Page" : catalogItem != null ? catalogItem.name : "Coin";

            var root = new GameObject(label);
            Undo.RegisterCreatedObjectUndo(root, "Place Ground Item");
            root.transform.SetPositionAndRotation(point, facing * Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));

            // The item's own gear model if it has one, else the model set for its kind.
            GameObject look = GearModel(catalogItem, root.transform);
            if (look == null && model != null)
            {
                look = (GameObject)PrefabUtility.InstantiatePrefab(model, root.transform);
            }
            else if (look == null && catalogItem is { kind: ItemKind.Light })
            {
                look = StandInLight(catalogItem, root.transform);
            }
            else if (look == null && page)
            {
                // Until a model is set for pages (or the map): a flat sheet of old paper.
                look = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Object.DestroyImmediate(look.GetComponent<Collider>());
                look.transform.SetParent(root.transform, false);
                look.transform.localScale = new Vector3(0.75f, 0.01f, 1f);
                var renderer = look.GetComponent<Renderer>();
                // Its own copy, saved in the scene, like the stand-in torch's.
                var paper = new Material(renderer.sharedMaterial) { name = "Journal Page (stand-in)" };
                paper.color = new Color(0.84f, 0.77f, 0.62f);
                if (paper.HasProperty("_BaseColor"))
                    paper.SetColor("_BaseColor", new Color(0.84f, 0.77f, 0.62f));
                renderer.sharedMaterial = paper;
            }
            else if (look == null)
            {
                // Until a model is set for this kind: a small sack-coloured lump.
                look = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                Object.DestroyImmediate(look.GetComponent<Collider>());
                look.transform.SetParent(root.transform, false);
                look.transform.localScale = new Vector3(1f, 0.7f, 1f);
                Debug.Log($"[Interactables] No model set for {kind} items — using a placeholder. Set one in the window.");
            }
            look.name = "Model";
            Glow(look);
            Cutouts(look);

            // Fit to Size by its longest side, and sit it on the ground.
            var bounds = RendererBounds(look);
            if (size > 0f && bounds.size.sqrMagnitude > 0f)
            {
                float longest = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
                look.transform.localScale *= size / longest;
                bounds = RendererBounds(look);
            }
            look.transform.position += Vector3.up * (point.y - bounds.min.y);

            AddBoxCollider(root, trigger: true, minSize: 0.5f);
            if (page)
            {
                var pickup = root.AddComponent<JournalPagePickup>();
                pickup.displayName = label;
                pickup.page = journalPage;
                root.name = $"Journal Page {journalPage}";
                Selection.activeGameObject = root;
                EditorSceneManager.MarkSceneDirty(root.scene);
                return;
            }
            if (map)
            {
                root.AddComponent<MapPickup>().displayName = label;
                Selection.activeGameObject = root;
                EditorSceneManager.MarkSceneDirty(root.scene);
                return;
            }
            var ground = root.AddComponent<GroundItem>();
            ground.item = catalogItem != null ? catalogItem.name : "";
            ground.count = count;
            ground.copper = copper;
            ground.displayName = catalogItem != null ? catalogItem.name : (copper >= 10000 ? "Pouch of Gold" : "Pouch of Coin");

            Selection.activeGameObject = root;
            EditorSceneManager.MarkSceneDirty(root.scene);
        }

        /// <summary>
        /// The item's own model from Harvest Gear's library — the meshes a weapon, shield, helm or
        /// shoulder rides a bone with — laid flat, side by side if there are several (a pair of
        /// shoulders). Null when it has none (body armour is painted on or skinned), so the
        /// kind's model is used instead.
        /// </summary>
        static GameObject GearModel(Item item, Transform parent) =>
            GroundItem.GearModel(item != null ? GearHarvester.LoadLibrary()?.For(item) : null, parent);

        /// <summary>
        /// A light with no model of its own or set for its kind: the exported lantern for the
        /// lantern, and for a torch a plain stick stood in the ground. Null for oil (a lump, like
        /// any item without a model).
        /// </summary>
        static GameObject StandInLight(Item item, Transform parent)
        {
            if (item.IsLantern)
            {
                var path = LightSourceSetup.FindModels().FirstOrDefault(p => NightLight.KindFromName(Path.GetFileNameWithoutExtension(p)) == NightLight.Kind.Lantern);
                var prefab = path != null ? AssetDatabase.LoadAssetAtPath<GameObject>(path) : null;
                return prefab != null ? (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent) : null;
            }
            if (!item.IsTorch)
                return null;

            var stick = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Object.DestroyImmediate(stick.GetComponent<Collider>());
            stick.transform.SetParent(parent, false);
            stick.transform.localScale = new Vector3(0.1f, 1f, 0.1f);
            var renderer = stick.GetComponent<Renderer>();
            // Its own copy, saved in the scene: tinting the shared default would turn every primitive brown.
            var wood = new Material(renderer.sharedMaterial) { name = "Torch (stand-in)" };
            wood.color = new Color(0.3f, 0.2f, 0.12f);
            if (wood.HasProperty("_BaseColor"))
                wood.SetColor("_BaseColor", new Color(0.3f, 0.2f, 0.12f));
            renderer.sharedMaterial = wood;
            return stick;
        }

        static PickupKind KindOf(Item item)
        {
            if (item == null) return PickupKind.Coin;
            if (item.IsPotion) return PickupKind.Potion;
            return item.kind switch
            {
                ItemKind.Weapon => PickupKind.Weapon,
                ItemKind.Armor => PickupKind.Armor,
                ItemKind.Shield => PickupKind.Shield,
                ItemKind.Clothing => PickupKind.Clothing,
                ItemKind.Jewelry => PickupKind.Jewelry,
                ItemKind.Tool => PickupKind.Tool,
                ItemKind.Light => PickupKind.Light,
                _ => PickupKind.TradeGood,
            };
        }

        // --- Shared --------------------------------------------------------------------------

        /// <summary>The point under the Scene view's centre, and a facing toward the camera.</summary>
        internal static bool Ground(out Vector3 point, out Quaternion facing)
        {
            point = default;
            facing = Quaternion.identity;
            var view = SceneView.lastActiveSceneView;
            if (view == null)
            {
                Debug.LogWarning("[Interactables] Open a Scene view and look at the spot.");
                return false;
            }
            var camera = view.camera;
            var ray = new Ray(camera.transform.position, camera.transform.forward);
            if (!Physics.Raycast(ray, out var hit, 2000f, ~0, QueryTriggerInteraction.Ignore))
            {
                if (!Physics.Raycast(view.pivot + Vector3.up * 50f, Vector3.down, out hit, 200f, ~0, QueryTriggerInteraction.Ignore))
                {
                    Debug.LogWarning("[Interactables] Nothing under the Scene view's centre to put it on.");
                    return false;
                }
            }
            point = hit.point;
            var toward = camera.transform.position - point;
            toward.y = 0f;
            facing = toward.sqrMagnitude > 0.01f ? Quaternion.LookRotation(toward, Vector3.up) : Quaternion.identity;
            return true;
        }

        internal static Bounds RendererBounds(GameObject root, bool solidOnly = false)
        {
            var renderers = root.GetComponentsInChildren<Renderer>(true)
                .Where(r => !solidOnly || r.sharedMaterials.All(m => m == null || !m.name.EndsWith("_Glow"))).ToArray();
            if (renderers.Length == 0)
                return new Bounds(root.transform.position, Vector3.zero);
            var bounds = renderers[0].bounds;
            foreach (var r in renderers.Skip(1))
                bounds.Encapsulate(r.bounds);
            return bounds;
        }

        /// <summary>A box around the renderers (light shafts left out), in the root's space.</summary>
        internal static void AddBoxCollider(GameObject root, bool trigger, float minSize)
        {
            var renderers = root.GetComponentsInChildren<Renderer>(true)
                .Where(r => r.sharedMaterials.All(m => m == null || !m.name.EndsWith("_Glow"))).ToArray();
            var box = root.AddComponent<BoxCollider>();
            box.isTrigger = trigger;
            if (renderers.Length == 0)
                return;
            var world = renderers[0].bounds;
            foreach (var r in renderers.Skip(1))
                world.Encapsulate(r.bounds);
            var t = root.transform;
            box.center = t.InverseTransformPoint(world.center);
            var size = t.InverseTransformVector(world.size);
            float floor = minSize / Mathf.Max(t.lossyScale.x, 0.0001f);
            box.size = new Vector3(Mathf.Max(Mathf.Abs(size.x), floor), Mathf.Max(Mathf.Abs(size.y), floor),
                                   Mathf.Max(Mathf.Abs(size.z), floor));
        }
    }

    /// <summary>WoW Sandbox → Place Treasure Chest: pick a chest model and its height, then place it.</summary>
    public class ChestPlacer : EditorWindow
    {
        [SerializeField] int choice;
        [SerializeField] float height = 0.9f;

        [MenuItem("WoW Sandbox/Place Treasure Chest")]
        static void Open()
        {
            var window = GetWindow<ChestPlacer>(true, "Place Treasure Chest");
            window.minSize = new Vector2(380f, 150f);
        }

        void OnGUI()
        {
            var models = InteractableSetup.ChestModels();
            if (models.Length == 0)
            {
                EditorGUILayout.HelpBox("No chests exported. In wow.export, export from world/skillactivated/containers " +
                                        "(treasurechest01, 02, ...) as glTF.", MessageType.Warning);
                return;
            }
            EditorGUILayout.HelpBox("Placed where the Scene view is looking. Set its contents in the Inspector.", MessageType.Info);
            choice = Mathf.Clamp(choice, 0, models.Length - 1);
            choice = EditorGUILayout.Popup("Chest", choice, models.Select(Path.GetFileNameWithoutExtension).ToArray());
            height = EditorGUILayout.FloatField(new GUIContent("Height", "How tall it stands, in world units (the warrior is about 1.9)."), height);
            if (GUILayout.Button("Place", GUILayout.Height(28f)))
                InteractableSetup.PlaceChest(models[choice], height);
        }
    }
}
