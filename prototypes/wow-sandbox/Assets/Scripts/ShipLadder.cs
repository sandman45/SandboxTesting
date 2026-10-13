using System.Collections.Generic;
using UnityEngine;

namespace WowSandbox
{
    /// <summary>
    /// A rope ladder hanging down a ship's side into the sea. WowCharacterController climbs it:
    /// swim up to it facing the hull and hold W to go up, S to go down, Space to let go. At
    /// the top you clamber over the rail onto the deck at <see cref="stepOff"/>; from the deck,
    /// walk to that spot facing the sea and press W to climb back down.
    ///
    /// The object sits at the top of the ladder, on the rail, with forward pointing out to
    /// sea; the ladder hangs straight down from there. Everything is in world units, so the
    /// object is kept at a world scale of 1 whatever the ship is scaled to. Being a child of
    /// the ship, it bobs and rolls with it, and so does whoever is on it.
    /// WoW Sandbox → Add Ship Ladders places a pair and builds the ropes and rungs.
    /// </summary>
    public class ShipLadder : MonoBehaviour
    {
        public static readonly List<ShipLadder> All = new();

        [Tooltip("How far it hangs down from the rail, in world units.")]
        public float length = 10f;
        [Tooltip("Width between the two ropes.")]
        public float width = 1f;
        [Tooltip("Where you land on deck after climbing over the rail.")]
        public Transform stepOff;

        void OnEnable() => All.Add(this);
        void OnDisable() => All.Remove(this);

        /// <summary>Out to sea from the ship's side, level.</summary>
        public Vector3 Outward
        {
            get
            {
                var outward = transform.forward;
                outward.y = 0f;
                return outward.sqrMagnitude > 0.0001f ? outward.normalized : Vector3.forward;
            }
        }

        /// <summary>Where a climber's feet go <paramref name="height"/> up from the ladder's foot,
        /// standing <paramref name="standOff"/> out from it.</summary>
        public Vector3 FeetAt(float height, float standOff) =>
            transform.position + transform.up * (height - length) + Outward * standOff;

        /// <summary>
        /// Close enough to grab from the water: in front of the ladder, not off to one side,
        /// and somewhere along its hanging length. <paramref name="height"/> is where on it.
        /// </summary>
        public bool InReachFromWater(Vector3 feet, float radius, out float height)
        {
            Vector3 offset = feet - transform.position;
            float side = Vector3.Dot(offset, transform.right);
            float outward = Vector3.Dot(offset, Outward);
            height = Mathf.Max(offset.y + length, 0f);

            return Mathf.Abs(side) < width * 0.5f + radius + 0.3f
                && outward > -0.2f && outward < radius + 1.5f
                && offset.y > -length - 2f && offset.y < -0.3f;
        }

        /// <summary>Standing on deck at the top of the ladder, where climbing down starts.</summary>
        public bool InReachFromDeck(Vector3 feet, float radius)
        {
            if (stepOff == null)
                return false;
            Vector3 offset = feet - stepOff.position;
            float vertical = offset.y;
            offset.y = 0f;
            return offset.magnitude < radius * 2f + 0.8f && Mathf.Abs(vertical) < 1f;
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawLine(transform.position, FeetAt(0f, 0f));
            Gizmos.DrawLine(transform.position, transform.position + Outward * 1.5f);
            if (stepOff != null)
            {
                Gizmos.DrawWireSphere(stepOff.position, 0.3f);
                Gizmos.DrawLine(transform.position, stepOff.position);
            }
        }
    }
}
