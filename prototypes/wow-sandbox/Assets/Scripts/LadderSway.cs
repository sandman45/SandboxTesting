using UnityEngine;

namespace WowSandbox
{
    /// <summary>
    /// Sways the lower part of a draped rope ladder (the mesh Add Ship Ladders builds), like
    /// WoW's own ropeladder01 swinging at its foot: the top stays hooked on the rail, and the
    /// swing grows toward the bottom — along the hull and a little away from it, never into it.
    /// Works on its own copy of the mesh, and only while it's on screen. Climbers aren't moved
    /// by it; the swing is a hand's width at most.
    /// </summary>
    [RequireComponent(typeof(MeshFilter))]
    public class LadderSway : MonoBehaviour
    {
        [Tooltip("Widest swing along the hull, at the foot, in world units.")]
        public float sideways = 0.08f;
        [Tooltip("Widest swing away from the hull, at the foot.")]
        public float outward = 0.04f;
        [Tooltip("Seconds per swing.")]
        public float period = 3.5f;
        [Tooltip("Local height (the top is 0) below which it swings; above it, it lies still.")]
        public float freeBelow = -1f;
        [Tooltip("Local height of the foot.")]
        public float bottom = -10f;

        Mesh _mesh;
        Vector3[] _rest;
        Vector3[] _moved;
        Renderer _renderer;
        float _phase;

        void Awake()
        {
            var filter = GetComponent<MeshFilter>();
            if (filter.sharedMesh == null)
            {
                enabled = false;
                return;
            }
            _mesh = Instantiate(filter.sharedMesh);
            _mesh.MarkDynamic();
            filter.sharedMesh = _mesh;
            _rest = _mesh.vertices;
            _moved = new Vector3[_rest.Length];
            var bounds = _mesh.bounds;
            bounds.Expand(2f * (sideways + outward));
            _mesh.bounds = bounds;
            _renderer = GetComponent<Renderer>();
            // Ladders side by side shouldn't swing in step.
            _phase = Random.value * 10f;
        }

        void Update()
        {
            if (_renderer != null && !_renderer.isVisible)
                return;

            float t = Time.time * (2f * Mathf.PI / Mathf.Max(period, 0.1f)) + _phase;
            float across = (Mathf.Sin(t) + 0.3f * Mathf.Sin(t * 2.3f)) * sideways / 1.3f;
            float away = (0.5f + 0.5f * Mathf.Sin(t * 0.7f + 1f)) * outward;
            float span = Mathf.Max(freeBelow - bottom, 0.01f);

            for (int i = 0; i < _rest.Length; i++)
            {
                var v = _rest[i];
                float k = Mathf.Clamp01((freeBelow - v.y) / span);
                k *= k;
                _moved[i] = new Vector3(v.x + across * k, v.y, v.z + away * k);
            }
            _mesh.vertices = _moved;
        }

        void OnDestroy()
        {
            if (_mesh != null)
                Destroy(_mesh);
        }
    }
}
