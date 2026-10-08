using UnityEngine;

namespace WowSandbox
{
    /// <summary>
    /// Puts a character export's helm back on its head. wow.export writes equipped helms
    /// ("Head_Item…" nodes) as unskinned meshes in the item's own space, applying the head
    /// attachment's offset only when Apply Pose is ticked — and even then baked, so it
    /// wouldn't follow the head. Unfixed, the helm renders at the model's origin: at the
    /// character's feet.
    ///
    /// The pose is fitted in the editor (HelmFitter, from the head's own geometry) and
    /// stored here; on Awake each helm piece is re-parented under the head bone at that
    /// pose, so it rides along with every animation from then on.
    /// </summary>
    public class HelmAttachment : MonoBehaviour
    {
        public Transform headBone;
        public Transform[] pieces;
        [Tooltip("Each piece's pose relative to the head bone, fitted in the editor.")]
        public Vector3 localPosition;
        public Quaternion localRotation = Quaternion.identity;
        public Vector3 localScale = Vector3.one;

        void Awake()
        {
            if (headBone == null || pieces == null)
                return;

            foreach (var piece in pieces)
            {
                if (piece == null)
                    continue;
                piece.SetParent(headBone, false);
                piece.SetLocalPositionAndRotation(localPosition, localRotation);
                piece.localScale = localScale;
            }
        }
    }
}
