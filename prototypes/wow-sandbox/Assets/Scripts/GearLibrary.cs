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

        public GearLook Find(string itemName) =>
            string.IsNullOrEmpty(itemName) ? null : looks.FirstOrDefault(look => look != null && look.itemName == itemName);

        /// <summary>
        /// What <paramref name="item"/> looks like: its own look if it has one (a legendary —
        /// Ashbringer), else its base item's, so "+1 Longsword of Strength" looks like a Longsword.
        /// </summary>
        public GearLook For(Item item) => item == null ? null : Find(item.name) ?? Find(item.baseName);
    }
}
