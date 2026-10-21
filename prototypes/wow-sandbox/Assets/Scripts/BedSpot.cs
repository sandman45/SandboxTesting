using UnityEngine;

namespace WowSandbox
{
    /// <summary>
    /// Marks where someone sleeps — a vendor's <see cref="Vendor.bed"/>. They lie down here
    /// facing its forward (the blue arrow); the gizmo is a body-length outline to drag into place.
    /// </summary>
    public class BedSpot : MonoBehaviour
    {
        void OnDrawGizmos()
        {
            Gizmos.color = new Color(0.4f, 0.7f, 1f, 0.8f);
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawWireCube(new Vector3(0f, 0.15f, 0f), new Vector3(0.8f, 0.3f, 1.9f));
            Gizmos.DrawLine(Vector3.up * 0.3f, new Vector3(0f, 0.3f, 1.2f));
        }
    }
}
