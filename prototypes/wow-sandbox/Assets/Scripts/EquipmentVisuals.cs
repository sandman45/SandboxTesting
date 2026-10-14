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
    ///   pasted over it (pieces never share pixels, so order doesn't matter);
    /// - shows or hides body pieces (a hat hides the hair, a shirt adds sleeve cuffs);
    /// - spawns separate meshes: helms on their bone, skinned armour bound to the skeleton.
    /// Items without a look leave that part of the body bare.
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
        Transform _pieceParent;

        Material _bodyMaterial;
        Texture _bareTexture;
        Texture2D _painted;
        Color32[] _basePixels;

        static readonly EquipSlot[] Slots = ((EquipSlot[])Enum.GetValues(typeof(EquipSlot)))
            .Where(slot => slot != EquipSlot.None).ToArray();

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

            var looks = Slots.Select(slot => library.Find(_inventory.Equipped(slot)?.baseName))
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

            foreach (var look in looks)
            {
                foreach (var piece in look.skinnedPieces)
                {
                    // A body piece the model already has (just switched off) is shown by ShowPieces.
                    if (piece.mesh == null || _pieces.ContainsKey(piece.name))
                        continue;
                    SpawnSkinned(piece);
                }
                foreach (var piece in look.attachedPieces)
                {
                    if (piece.mesh != null)
                        SpawnAttached(piece);
                }
            }
        }

        void SpawnSkinned(SkinnedPiece piece)
        {
            var bones = new Transform[piece.boneNames.Length];
            for (int i = 0; i < bones.Length; i++)
            {
                if (!_bones.TryGetValue(piece.boneNames[i], out bones[i]))
                {
                    Debug.LogWarning($"[EquipmentVisuals] {piece.name}: the model has no bone \"{piece.boneNames[i]}\" — skipped.", this);
                    return;
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
            _spawned.Add(go);
        }

        void SpawnAttached(AttachedPiece piece)
        {
            if (!_bones.TryGetValue(piece.bone, out var bone))
            {
                Debug.LogWarning($"[EquipmentVisuals] {piece.name}: the model has no bone \"{piece.bone}\" — skipped.", this);
                return;
            }

            var go = new GameObject(piece.name);
            go.transform.SetParent(bone, false);
            go.transform.SetLocalPositionAndRotation(piece.localPosition, piece.localRotation);
            go.transform.localScale = piece.localScale;
            go.AddComponent<MeshFilter>().sharedMesh = piece.mesh;
            go.AddComponent<MeshRenderer>().sharedMaterials = piece.materials;
            _spawned.Add(go);
        }
    }
}
