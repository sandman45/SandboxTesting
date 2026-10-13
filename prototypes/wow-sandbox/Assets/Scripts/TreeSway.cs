using UnityEngine;

namespace WowSandbox
{
    /// <summary>
    /// Sways a palm in the wind. WoW does this in the doodad shader, so the export carries no
    /// sway at all — one bone and an empty Stand clip. This rocks that bone about the foot of
    /// the tree instead: a lazy breeze in fine weather, building with StormWeather's intensity
    /// to a hard lean downwind with gusts, along its wind direction.
    ///
    /// The whole tree leans as one piece rather than bending, which reads fine at palm
    /// heights. Only the skinned mesh moves: the colliders (baked from the rest pose, on the
    /// renderer's own GameObject) stay put, so nothing gets pushed around. Taller trees rock
    /// slower, and each has its own phase so a grove doesn't move in lockstep.
    /// </summary>
    [DefaultExecutionOrder(100)] // after the glTF's Animator, which owns the bone
    public class TreeSway : MonoBehaviour
    {
        [Tooltip("Degrees each way in calm weather.")]
        public float calmSway = 1.2f;
        [Tooltip("Degrees it leans downwind at the height of a storm…")]
        public float stormLean = 5f;
        [Tooltip("…and how far gusts throw it either side of that.")]
        public float stormSway = 4f;
        [Tooltip("Sway cycles per second for a 10-unit tree; taller ones go slower.")]
        public float frequency = 0.35f;
        [Tooltip("Wind direction (degrees, 0 = +Z) when there's no StormWeather in the scene.")]
        public float fallbackWindDirection = 30f;

        Transform _bone;
        Vector3 _restPosition;
        Quaternion _restRotation;
        float _speed;
        float _phase;
        float _seed;

        void Start()
        {
            var skinned = GetComponentInChildren<SkinnedMeshRenderer>();
            _bone = skinned != null && skinned.bones.Length > 0 ? skinned.bones[0] : null;
            if (_bone == null)
            {
                Debug.LogWarning($"[TreeSway] {name} has no skinned bone to sway.", this);
                enabled = false;
                return;
            }

            _restPosition = _bone.localPosition;
            _restRotation = _bone.localRotation;

            float height = Mathf.Max(skinned.bounds.size.y, 1f);
            _speed = frequency * Mathf.Sqrt(10f / height);
            _phase = Random.value * Mathf.PI * 2f;
            _seed = Random.value * 100f;
        }

        void LateUpdate()
        {
            var weather = StormWeather.Active;
            float storm = weather != null ? weather.intensity : 0f;
            float direction = (weather != null ? weather.windDirection : fallbackWindDirection) * Mathf.Deg2Rad;
            var downwind = new Vector3(Mathf.Sin(direction), 0f, Mathf.Cos(direction));

            // A swell of sway plus slower, uneven gusts — Perlin so they come and go.
            float t = Time.time;
            float wave = Mathf.Sin(t * _speed * Mathf.PI * 2f + _phase);
            float gust = Mathf.PerlinNoise(_seed, t * 0.25f) * 2f - 1f;
            float sway = Mathf.Lerp(calmSway, stormSway, storm);
            float angle = storm * stormLean + sway * (wave * 0.7f + gust * 0.6f);

            // Restore the rest pose, then rock it about the foot of the tree, tipping downwind.
            _bone.localPosition = _restPosition;
            _bone.localRotation = _restRotation;
            var tilt = Quaternion.AngleAxis(angle, Vector3.Cross(Vector3.up, downwind));
            var foot = transform.position;
            _bone.SetPositionAndRotation(foot + tilt * (_bone.position - foot), tilt * _bone.rotation);
        }

        void OnDisable()
        {
            if (_bone == null)
                return;
            _bone.localPosition = _restPosition;
            _bone.localRotation = _restRotation;
        }
    }
}
