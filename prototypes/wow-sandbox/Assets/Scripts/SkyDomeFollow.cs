using UnityEngine;

namespace WowSandbox
{
    /// <summary>
    /// Keeps a sky dome centred on the camera so its edge can never be reached.
    ///
    /// Position only — deliberately not rotation. Inheriting the camera's rotation would
    /// drag the sky around as you turn, which reads as the world spinning rather than you.
    /// </summary>
    [ExecuteAlways]
    public class SkyDomeFollow : MonoBehaviour
    {
        [Tooltip("Leave empty to track the main camera.")]
        public Transform target;

        [Tooltip("Raise or lower the dome relative to the camera. WoW domes sit with the " +
                 "horizon slightly below eye level.")]
        public float heightOffset;

        void LateUpdate()
        {
            var follow = target;
            if (follow == null)
            {
                var camera = Camera.main;
                if (camera == null)
                    return;
                follow = camera.transform;
            }

            transform.position = follow.position + Vector3.up * heightOffset;
        }
    }
}
