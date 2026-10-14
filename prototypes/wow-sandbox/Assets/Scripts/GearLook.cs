using System;
using UnityEngine;

namespace WowSandbox
{
    /// <summary>
    /// What one item looks like on the player, harvested from a wow.export character export of
    /// the player's model wearing just that item (GearHarvester). Gear comes in three kinds,
    /// and one item can use several:
    /// - Painted on: cloth shirts, trousers and sandals are drawn into the body texture.
    ///   <see cref="textureLayer"/> holds the pixels that differ from the bare body, cropped to
    ///   <see cref="textureRegion"/>; its alpha is the mask (255 = this item's pixel).
    /// - Body pieces: WoW swaps parts of the body mesh to match gear (sleeve cuffs for a shirt,
    ///   no hair under a hat). Named without the model's prefix, e.g. "Wrists2", "Hair2".
    /// - Separate meshes: helms ride a bone (<see cref="attachedPieces"/>); modern armour is
    ///   skinned to the skeleton (<see cref="skinnedPieces"/>, also used for added body pieces).
    /// </summary>
    public class GearLook : ScriptableObject
    {
        [Tooltip("The item's baseName, so magic copies (\"+1 Pirate Hat\") share the look.")]
        public string itemName;

        [Header("Painted on")]
        public Texture2D textureLayer;
        public RectInt textureRegion;

        [Header("Body pieces")]
        public string[] addsPieces = Array.Empty<string>();
        public string[] hidesPieces = Array.Empty<string>();

        [Header("Separate meshes")]
        public SkinnedPiece[] skinnedPieces = Array.Empty<SkinnedPiece>();
        public AttachedPiece[] attachedPieces = Array.Empty<AttachedPiece>();

        public bool HasTexture => textureLayer != null && textureRegion.width > 0 && textureRegion.height > 0;
    }

    /// <summary>A mesh skinned to the player's own skeleton, rebound to its bones by name.</summary>
    [Serializable]
    public class SkinnedPiece
    {
        public string name;
        public Mesh mesh;
        [Tooltip("A material named as the library's body material is swapped for the player's own, so it shows the worn gear.")]
        public Material[] materials;
        public string[] boneNames;
        public string rootBone;
    }

    /// <summary>A rigid mesh parented to one bone at a fitted pose.</summary>
    [Serializable]
    public class AttachedPiece
    {
        public string name;
        public Mesh mesh;
        public Material[] materials;
        public string bone;
        public Vector3 localPosition;
        public Quaternion localRotation = Quaternion.identity;
        public Vector3 localScale = Vector3.one;
    }
}
