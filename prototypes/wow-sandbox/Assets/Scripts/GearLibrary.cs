using System;
using System.Linq;
using UnityEngine;

namespace WowSandbox
{
    /// <summary>
    /// Every harvested GearLook for one player model, plus what EquipmentVisuals needs to dress
    /// it: the bare body texture to paint gear onto, and how to find the body on the model.
    /// Built by WoW Sandbox → Harvest Gear; lives with the other WoW-derived assets under
    /// Assets/WowExports/ (gitignored).
    /// </summary>
    public class GearLibrary : ScriptableObject
    {
        [Tooltip("The bare body texture, imported readable so gear can be painted onto a copy.")]
        public Texture2D baseBody;
        [Tooltip("Name of the model's body material (glTF material name, e.g. \"data-1\").")]
        public string bodyMaterialName;
        [Tooltip("The body material's texture property that holds the body texture.")]
        public string bodyTextureProperty;
        [Tooltip("Prefix on every body-piece object, e.g. \"thinhumanmale_\" in \"thinhumanmale_Hair2\".")]
        public string piecePrefix;

        public GearLook[] looks = Array.Empty<GearLook>();

        [Tooltip("WoW's sheath attachment points on this model, for weapons and shields put away (Z).")]
        public SheathPoint[] sheathPoints = Array.Empty<SheathPoint>();

        public SheathPoint Sheath(SheathSpot spot) => sheathPoints.FirstOrDefault(point => point != null && point.spot == spot);

        public GearLook Find(string itemName) =>
            string.IsNullOrEmpty(itemName) ? null : looks.FirstOrDefault(look => look != null && look.itemName == itemName);

        /// <summary>
        /// What <paramref name="item"/> looks like: its own look if it has one (a legendary —
        /// Ashbringer), else its base item's, so "+1 Longsword of Strength" looks like a Longsword.
        /// </summary>
        public GearLook For(Item item) => item == null ? null : Find(item.name) ?? Find(item.baseName);
    }

    /// <summary>Where a put-away weapon or shield hangs, as WoW does it.</summary>
    public enum SheathSpot
    {
        /// <summary>A two-hander, across the back.</summary>
        Back,
        /// <summary>A shield, flat on the back.</summary>
        Shield,
        /// <summary>A one-hander or dagger, on the left hip (drawn across the body).</summary>
        Hip,
    }

    /// <summary>
    /// A sheath attachment point: the pose, relative to its bone, of an item mesh put there. Any
    /// item fits it as is — an item mesh's origin is its attachment point, in the model's axes
    /// at rest — so one per spot does for every weapon.
    /// </summary>
    [Serializable]
    public class SheathPoint
    {
        public SheathSpot spot;
        public string bone;
        public Vector3 localPosition;
        public Quaternion localRotation = Quaternion.identity;
        public Vector3 localScale = Vector3.one;
    }
}
