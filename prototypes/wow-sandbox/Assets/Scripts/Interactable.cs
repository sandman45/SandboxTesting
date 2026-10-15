using System.Collections.Generic;
using UnityEngine;

namespace WowSandbox
{
    /// <summary>
    /// Something in the world the player can use by clicking it within reach: a treasure
    /// chest, an item lying on the ground. Hovering shows its name and what clicking does;
    /// clicking from too far says so. The player's Interactor does the hovering and clicking
    /// (added to the player by the first interactable that wakes up), and calls Interact.
    /// Needs a collider to be clicked.
    /// </summary>
    public abstract class Interactable : MonoBehaviour
    {
        public static readonly List<Interactable> All = new();

        [Tooltip("The name shown on hover.")]
        public string displayName = "Treasure Chest";
        [Tooltip("How close you need to be, from its nearest edge.")]
        public float range = 2.5f;

        /// <summary>What a click does, for the hover hint: "Open", "Pick up".</summary>
        public abstract string Verb { get; }

        /// <summary>Whether it can be used now — false hides the hint (an emptied chest, say).</summary>
        public virtual bool CanInteract => true;

        /// <summary>Use it. <paramref name="player"/> is in reach and facing it.</summary>
        public abstract void Interact(GameObject player);

        protected virtual void OnEnable()
        {
            All.Add(this);
            Interactor.EnsureOnPlayer();
        }

        protected virtual void OnDisable() => All.Remove(this);

        /// <summary>Distance from <paramref name="point"/> to its nearest edge (its colliders), or to it.</summary>
        public float DistanceFrom(Vector3 point)
        {
            float best = float.MaxValue;
            foreach (var collider in GetComponentsInChildren<Collider>())
            {
                if (!collider.enabled)
                    continue;
                best = Mathf.Min(best, Vector3.Distance(point, collider.ClosestPointOnBounds(point)));
            }
            return best < float.MaxValue ? best : Vector3.Distance(point, transform.position);
        }
    }
}
