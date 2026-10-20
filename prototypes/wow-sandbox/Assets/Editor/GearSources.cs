using System;
using System.Collections.Generic;
using UnityEngine;

namespace WowSandbox.EditorTools
{
    /// <summary>
    /// Which export dresses which item, for Harvest Gear. Harvest Gear fills it in: each run adds
    /// a row for every export it hasn't seen — a folder under character/thinhuman/ or an item
    /// model under WowExports/item/ — with its best guess at the item from the export's name.
    /// Edit a row in the Inspector to fix a guess, fill in one it couldn't make, or clear the
    /// item to leave that export out. Lives at Assets/WowExports/GearSources.asset (gitignored
    /// with the exports it points at).
    /// </summary>
    public class GearSources : ScriptableObject
    {
        [Serializable]
        public class Source
        {
            [Tooltip("A folder under character/thinhuman/ (the model wearing just this item), or an item model's .gltf path.")]
            public string export;
            [Tooltip("The catalog item it dresses, e.g. \"Longsword\". Empty leaves the export out.")]
            public string item;
            [Tooltip("Item models only: Main-hand, Off-hand, Off-hand (held), Head, Back, Shoulder (L) or Shoulder (R). Empty follows the item.")]
            public string slot;
            [Tooltip("Size of the item's separate meshes (helms, shoulders, weapons), each about its own " +
                     "attachment point: 0.85 is a little smaller. 1 (or 0) uses Harvest Gear's built-in fit " +
                     "for the item, if it has one, else full size.")]
            public float scale = 1f;
            [Tooltip("Moves the item's separate meshes, in the character's own axes (x right, y up, z forward; " +
                     "he's about 1.9 tall): (0, -0.03, 0) lowers a helm a little. Zero uses the built-in fit.")]
            public Vector3 offset;
        }

        public List<Source> sources = new();
    }
}
