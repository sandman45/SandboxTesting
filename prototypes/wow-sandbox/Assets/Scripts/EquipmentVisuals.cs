using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace WowSandbox
{
    /// <summary>
    /// Dresses the player in whatever they have equipped, from the harvested GearLooks. On every
    /// equipment change it:
    /// - repaints the body texture: a copy of the bare body with each painted-on item's pixels
    ///   pasted over it, in WoW's layer order (legs, feet, chest, hands), so gloves cover a
    ///   shirt's sleeves — the one overlap among the pieces so far;
    /// - shows or hides body pieces (a hat hides the hair, a shirt adds sleeve cuffs);
    /// - spawns separate meshes: helms on their bone, skinned armour bound to the skeleton.
    /// Items without a look leave that part of the body bare. Hold shows a carried tool's look
    /// (the Fishing Pole while fishing) in place of the main-hand weapon until Release.
    /// SetSheathed puts the weapon and shield away on the library's sheath points, as WoW does:
    /// a two-hander across the back, a one-hander on the hip, a shield on the back.
    /// </summary>
    [RequireComponent(typeof(Inventory))]
    public class EquipmentVisuals : MonoBehaviour
    {
        public GearLibrary library;
        [Tooltip("The model under the player; defaults to WowCharacterController.modelRoot.")]
        public Transform model;

        Inventory _inventory;
        bool _ready;
        string _signature;

        readonly Dictionary<string, Transform> _bones = new();
        // Body pieces on the model as exported, by name without the prefix, and whether each was showing.
        readonly Dictionary<string, Renderer> _pieces = new();
        readonly Dictionary<string, bool> _shownAtStart = new();
        readonly List<GameObject> _spawned = new();
        // The main-hand weapon's meshes, hidden while a tool is held, and the tool's own.
        readonly List<GameObject> _mainHand = new();
        readonly List<GameObject> _held = new();
        // The shield's meshes, put away with the weapon (a light in the off hand stays in hand).
        readonly List<GameObject> _shield = new();
        // Each weapon or shield mesh's pose in hand, and its look's scale, for drawing it again.
        readonly Dictionary<GameObject, (Transform bone, Vector3 position, Quaternion rotation, Vector3 scale, float lookScale)> _inHand = new();
        Transform _pieceParent;

        /// <summary>Whether the weapon and shield are put away (WeaponSheath, Z).</summary>
        public bool Sheathed { get; private set; }

        /// <summary>
        /// How a put-away item sits on each sheath point, in the character's axes: a turn and a
        /// nudge (WeaponSheath's fits). WoW's points are only a place, and an item keeps the way
        /// it faces in hand at rest — blade forward, shield face out to the left.
        /// </summary>
        public Func<SheathSpot, (Quaternion turn, Vector3 offset)> SheathFit { get; set; }

        /// <summary>Whether a carried tool is in hand (Hold) — the Fishing Pole while fishing.</summary>
        public bool Holding => _held.Count > 0;

        Material _bodyMaterial;
        Texture _bareTexture;
        Texture2D _painted;
        Color32[] _basePixels;

        // Painting order, bottom layer first: wow.export's SLOT_LAYER (legs 10, feet 11, chest 13,
        // hands 20). Slots that paint nothing follow in any order.
        static readonly EquipSlot[] Slots = new[] { EquipSlot.Legs, EquipSlot.Feet, EquipSlot.Chest, EquipSlot.Hands }
            .Concat(((EquipSlot[])Enum.GetValues(typeof(EquipSlot))).Where(slot => slot != EquipSlot.None))
            .Distinct().ToArray();

        void Awake()
        {
            _inventory = GetComponent<Inventory>();
        }

        void OnEnable() => _inventory.Changed += Refresh;
        void OnDisable() => _inventory.Changed -= Refresh;

        void Start() => Refresh();

        void OnDestroy()
        {
            if (_painted != null)
                Destroy(_painted);
            if (_bodyMaterial != null)
                Destroy(_bodyMaterial);
        }

        void Refresh()
        {
            if (library == null || !Prepare())
                return;

            var looks = Slots.Select(slot => library.For(_inventory.Equipped(slot)))
                             .Where(look => look != null)
                             .Distinct()
                             .ToList();

            // Inventory.Changed also fires for bags and coin; only rebuild when the looks change.
            string signature = string.Join("|", looks.Select(look => look.name));
            if (signature == _signature)
                return;
            _signature = signature;

            Paint(looks);
            ShowPieces(looks);
            SpawnMeshes(looks);
        }

        /// <summary>
        /// Shows <paramref name="itemName"/>'s look in hand — a tool carried in the bags, like the
        /// Fishing Pole — with the main-hand weapon put away until Release. Nothing happens if
        /// that item has no look yet.
        /// </summary>
        public void Hold(string itemName)
        {
            Release();
            var look = library != null && Prepare() ? library.Find(itemName) : null;
            if (look == null)
                return;

            foreach (var piece in look.attachedPieces)
            {
                var go = piece.mesh != null ? SpawnAttached(piece, look) : null;
                if (go != null)
                    _held.Add(go);
            }
            // A weapon on the hip or back stays there; one in hand makes way for the tool.
            if (_held.Count > 0 && !Sheathed)
                SetActive(_mainHand, false);
        }

        /// <summary>
        /// Puts the weapon and shield away on their sheath points, or back in hand. A spot the
        /// library has no point for (it predates sheathing — re-run Harvest Gear) leaves that
        /// item in hand.
        /// </summary>
        public void SetSheathed(bool sheathed)
        {
            Sheathed = sheathed;
            var weapon = _inventory.Equipped(EquipSlot.MainHand);
            var spot = weapon is { twoHanded: true } ? SheathSpot.Back : SheathSpot.Hip;
            foreach (var go in _mainHand)
                Place(go, sheathed ? spot : null);
            foreach (var go in _shield)
                Place(go, sheathed ? SheathSpot.Shield : null);
            // A tool in hand hides the weapon only while that's in hand too.
            if (_held.Count > 0)
                SetActive(_mainHand, sheathed);
        }

        /// <summary>One weapon or shield mesh onto its sheath point, or (null) back in hand.</summary>
        void Place(GameObject go, SheathSpot? spot)
        {
            if (go == null || !_inHand.TryGetValue(go, out var hand))
                return;
            var point = spot is { } at && library != null ? library.Sheath(at) : null;
            if (point != null && _bones.TryGetValue(point.bone, out var bone))
            {
                var (turn, offset) = SheathFit?.Invoke(point.spot) ?? (Quaternion.identity, Vector3.zero);
                go.transform.SetParent(bone, false);
                go.transform.SetLocalPositionAndRotation(point.localPosition + point.localRotation * offset,
                    point.localRotation * turn);
                go.transform.localScale = point.localScale * hand.lookScale;
                return;
            }
            go.transform.SetParent(hand.bone, false);
            go.transform.SetLocalPositionAndRotation(hand.position, hand.rotation);
            go.transform.localScale = hand.scale;
        }

        /// <summary>Puts a held tool away and brings the main-hand weapon back.</summary>
        public void Release()
        {
            foreach (var go in _held)
                if (go != null)
                    Destroy(go);
            _held.Clear();
            SetActive(_mainHand, true);
        }

        static void SetActive(List<GameObject> objects, bool active)
        {
            foreach (var go in objects)
                if (go != null)
                    go.SetActive(active);
        }

        /// <summary>Finds the body, its material and bones the first time. False if it can't dress this model.</summary>
        bool Prepare()
        {
            if (_ready)
                return true;

            if (model == null && TryGetComponent<WowCharacterController>(out var movement))
                model = movement.modelRoot;
            if (model == null)
                return false;

            foreach (var t in model.GetComponentsInChildren<Transform>(true))
                _bones.TryAdd(t.name, t);

            // One shared copy of the body material, on every body piece, so painting it once
            // dresses them all (and the skinned pieces spawned later).
            foreach (var renderer in model.GetComponentsInChildren<Renderer>(true))
            {
                string objectName = renderer.gameObject.name;
                if (!string.IsNullOrEmpty(library.piecePrefix) && objectName.StartsWith(library.piecePrefix))
                {
                    string piece = objectName.Substring(library.piecePrefix.Length);
                    _pieces.TryAdd(piece, renderer);
                    _shownAtStart.TryAdd(piece, renderer.enabled && renderer.gameObject.activeSelf);
                    _pieceParent ??= renderer.transform.parent;
                }

                var materials = renderer.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < materials.Length; i++)
                {
                    if (!IsBodyMaterial(materials[i]))
                        continue;
                    if (_bodyMaterial == null)
                    {
                        _bodyMaterial = new Material(materials[i]) { name = materials[i].name + " (dressed)" };
                        _bareTexture = _bodyMaterial.GetTexture(library.bodyTextureProperty);
                    }
                    materials[i] = _bodyMaterial;
                    changed = true;
                }
                if (changed)
                    renderer.sharedMaterials = materials;
            }

            if (_bodyMaterial == null)
            {
                Debug.LogWarning($"[EquipmentVisuals] No \"{library.bodyMaterialName}\" material on {model.name} — " +
                                 "is the library from a different model? Re-run WoW Sandbox → Harvest Gear.", this);
                return false;
            }

            _ready = true;
            return true;
        }

        bool IsBodyMaterial(Material material) =>
            material != null && (material.name == library.bodyMaterialName ||
                                 material.name.StartsWith(library.bodyMaterialName + " "));

        void Paint(List<GearLook> looks)
        {
            var painted = looks.Where(look => look.HasTexture).ToList();
            if (painted.Count == 0 || library.baseBody == null)
            {
                _bodyMaterial.SetTexture(library.bodyTextureProperty, _bareTexture);
                return;
            }

            var bare = library.baseBody;
            int width = bare.width;
            _basePixels ??= bare.GetPixels32();
            var pixels = (Color32[])_basePixels.Clone();

            foreach (var look in painted)
            {
                var region = look.textureRegion;
                var layer = look.textureLayer.GetPixels32();
                for (int y = 0; y < region.height; y++)
                {
                    int from = y * region.width;
                    int to = (region.y + y) * width + region.x;
                    for (int x = 0; x < region.width; x++)
                    {
                        var pixel = layer[from + x];
                        if (pixel.a > 127)
                            pixels[to + x] = new Color32(pixel.r, pixel.g, pixel.b, pixels[to + x].a);
                    }
                }
            }

            if (_painted == null)
            {
                // sRGB like the texture it replaces; mipmapped, or it shimmers at a distance.
                _painted = new Texture2D(width, bare.height, TextureFormat.RGBA32, true, false)
                {
                    name = "Dressed body",
                    wrapMode = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Trilinear,
                    anisoLevel = _bareTexture != null ? Mathf.Max(1, _bareTexture.anisoLevel) : 1,
                };
            }
            _painted.SetPixels32(pixels);
            _painted.Apply(true);
            _bodyMaterial.SetTexture(library.bodyTextureProperty, _painted);
        }

        void ShowPieces(List<GearLook> looks)
        {
            var hidden = new HashSet<string>(looks.SelectMany(look => look.hidesPieces));
            var added = new HashSet<string>(looks.SelectMany(look => look.addsPieces));
            foreach (var (piece, renderer) in _pieces)
                renderer.enabled = (_shownAtStart[piece] || added.Contains(piece)) && !hidden.Contains(piece);
        }

        void SpawnMeshes(List<GearLook> looks)
        {
            foreach (var spawned in _spawned)
                if (spawned != null)
                    Destroy(spawned);
            _spawned.Clear();
            _mainHand.Clear();
            _shield.Clear();
            _inHand.Clear();

            var mainHand = library.For(_inventory.Equipped(EquipSlot.MainHand));
            var shield = _inventory.Equipped(EquipSlot.OffHand) is { kind: ItemKind.Shield } carried ? library.For(carried) : null;
            foreach (var look in looks)
            {
                var made = new List<GameObject>();
                foreach (var piece in look.skinnedPieces)
                {
                    // A body piece the model already has (just switched off) is shown by ShowPieces.
                    if (piece.mesh == null || _pieces.ContainsKey(piece.name))
                        continue;
                    made.Add(SpawnSkinned(piece));
                }
                foreach (var piece in look.attachedPieces)
                {
                    if (piece.mesh != null)
                        made.Add(SpawnAttached(piece, look));
                }
                made.RemoveAll(go => go == null);
                _spawned.AddRange(made);
                if (look == mainHand)
                    _mainHand.AddRange(made);
                if (look == shield)
                    _shield.AddRange(made);
            }

            foreach (var go in _mainHand)
                RememberHand(go, mainHand);
            foreach (var go in _shield)
                RememberHand(go, shield);

            // A new weapon or shield goes where the old one was: in hand, or put away.
            SetSheathed(Sheathed);
        }

        void RememberHand(GameObject go, GearLook look)
        {
            var t = go.transform;
            _inHand[go] = (t.parent, t.localPosition, t.localRotation, t.localScale,
                look.attachedScale > 0f ? look.attachedScale : 1f);
        }

        GameObject SpawnSkinned(SkinnedPiece piece)
        {
            var bones = new Transform[piece.boneNames.Length];
            for (int i = 0; i < bones.Length; i++)
            {
                if (!_bones.TryGetValue(piece.boneNames[i], out bones[i]))
                {
                    Debug.LogWarning($"[EquipmentVisuals] {piece.name}: the model has no bone \"{piece.boneNames[i]}\" — skipped.", this);
                    return null;
                }
            }

            var go = new GameObject(library.piecePrefix + piece.name);
            go.transform.SetParent(_pieceParent != null ? _pieceParent : model, false);
            var renderer = go.AddComponent<SkinnedMeshRenderer>();
            renderer.sharedMesh = piece.mesh;
            renderer.bones = bones;
            renderer.rootBone = _bones.TryGetValue(piece.rootBone ?? "", out var root) ? root : null;
            renderer.updateWhenOffscreen = true;
            renderer.sharedMaterials = piece.materials.Select(m => IsBodyMaterial(m) ? _bodyMaterial : m).ToArray();
            return go;
        }

        GameObject SpawnAttached(AttachedPiece piece, GearLook look)
        {
            if (!_bones.TryGetValue(piece.bone, out var bone))
            {
                Debug.LogWarning($"[EquipmentVisuals] {piece.name}: the model has no bone \"{piece.bone}\" — skipped.", this);
                return null;
            }

            var go = new GameObject(piece.name);
            go.transform.SetParent(bone, false);
            go.transform.SetLocalPositionAndRotation(piece.localPosition, piece.localRotation);
            go.transform.localScale = piece.localScale * (look.attachedScale > 0f ? look.attachedScale : 1f);
            go.AddComponent<MeshFilter>().sharedMesh = piece.mesh;
            go.AddComponent<MeshRenderer>().sharedMaterials = piece.materials;
            return go;
        }
    }
}
