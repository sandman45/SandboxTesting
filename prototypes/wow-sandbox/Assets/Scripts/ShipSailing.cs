using UnityEngine;

namespace WowSandbox
{
    /// <summary>
    /// Sails the floating ship. The helm sets Throttle and Steer each frame; this turns them
    /// into a sail setting that's let out or taken in gradually, a speed that follows it with
    /// a big ship's weight (slow to pick up, long to coast to a stop), and a rudder that only
    /// bites with way on — stopped, it can't turn at all. It heels a little into its turns.
    ///
    /// Moves only the ship's position across the water and its Heading; ShipBuoyancy still
    /// does the rise, fall, pitch and roll on the waves. Anything aboard rides along the way it
    /// always has. It stops dead rather than sail onto the island's shelf or off the edge of
    /// the sea — take in the sails (S) to back off again.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(ShipBuoyancy))]
    // Before ShipBuoyancy (-50), so the waves are sampled where the ship has moved to.
    [DefaultExecutionOrder(-60)]
    public class ShipSailing : MonoBehaviour
    {
        /// <summary>The ship in the scene, for the maps.</summary>
        public static ShipSailing Active { get; private set; }

        [Tooltip("Top speed at full sail, in world units per second. 0 = one and a half times the " +
                 "player's run speed.")]
        public float maxSpeed = 0f;
        [Tooltip("Seconds to gather way from a standstill to full speed.")]
        public float secondsToFullSpeed = 10f;
        [Tooltip("Seconds to coast to a stop from full speed with the sails taken in.")]
        public float secondsToCoastStop = 12f;
        [Tooltip("Seconds to stop from full speed once the anchor's dropped.")]
        public float secondsToAnchorStop = 4f;
        [Tooltip("How fast W and S let out and take in sail, in full sail settings per second.")]
        public float sailChangePerSecond = 0.35f;
        [Tooltip("How far S can take it astern, as a fraction of full sail — enough to back off a shoal.")]
        [Range(0f, 0.5f)] public float reverseSail = 0.2f;
        [Tooltip("Fastest turn, in degrees per second, with good way on.")]
        public float maxTurnRate = 7f;
        [Tooltip("Speed, as a fraction of top speed, the rudder needs for its full effect.")]
        [Range(0.05f, 1f)] public float steerageSpeed = 0.35f;
        [Tooltip("Seconds for the rudder to swing the bow from straight to a full turn.")]
        public float turnResponse = 1.5f;
        [Tooltip("Degrees it leans over in a full-speed turn.")]
        public float heelDegrees = 4f;
        [Tooltip("While the anchor's down, the sails stay furled and the ship holds its place.")]
        public bool anchored = true;

        /// <summary>Set by the helm each frame: +1 lets out sail, -1 takes it in.</summary>
        public float Throttle { get; set; }
        /// <summary>Set by the helm each frame: +1 steers to starboard (right), -1 to port.</summary>
        public float Steer { get; set; }

        /// <summary>The sail setting, -reverseSail (astern) to 1 (full sail).</summary>
        public float Sail => _sail;
        public float Speed => _speed;
        public float MaxSpeed => maxSpeed > 0f ? maxSpeed : _autoMaxSpeed;

        /// <summary>Level direction the bow points in.</summary>
        public Vector3 Bow
        {
            get
            {
                var bow = Quaternion.Euler(0f, _buoyancy.Heading, 0f) * _localBow;
                bow.y = 0f;
                return bow.sqrMagnitude > 0.0001f ? bow.normalized : transform.forward;
            }
        }

        /// <summary>The middle of the ship, for the camera to look at.</summary>
        public Vector3 Centre => transform.TransformPoint(_localCentre);

        /// <summary>Half the ship's length, bow to stern.</summary>
        public float HalfLength => _halfLength;

        ShipBuoyancy _buoyancy;
        WaterVolume _water;
        Vector3 _localBow;      // in the ship's own frame, level
        Vector3 _localCentre;
        float _halfLength = 20f;
        float _keelDepth = 5f;  // how far the keel sits below calm sea level
        float _autoMaxSpeed = 10f;
        bool _measured;

        float _sail;
        float _speed;
        float _turnRate;
        string _blockedReason;
        float _blockedMessageTime = -100f;

        void Awake() => _buoyancy = GetComponent<ShipBuoyancy>();
        void OnEnable() => Active = this;

        void OnDisable()
        {
            if (Active == this)
                Active = null;
        }

        void Start()
        {
            _water = FindFirstObjectByType<WaterVolume>();
            var player = FindAnyObjectByType<WowCharacterController>();
            if (player != null)
                _autoMaxSpeed = player.runSpeed * 1.5f;
        }

        /// <summary>Which way is the bow — the helm faces it. Without a helm, the ship's longer
        /// horizontal axis is assumed, pointing whichever way its forward does.</summary>
        public void SetBow(Vector3 worldDirection)
        {
            var local = transform.InverseTransformDirection(worldDirection);
            local.y = 0f;
            if (local.sqrMagnitude > 0.0001f)
            {
                _localBow = local.normalized;
                _measured = false;
            }
        }

        void Update()
        {
            if (_water == null)
                return;
            if (!_measured)
                Measure();

            float top = MaxSpeed;
            float dt = Time.deltaTime;

            if (anchored)
                _sail = 0f;
            else
                _sail = Mathf.Clamp(_sail + Throttle * sailChangePerSecond * dt, -reverseSail, 1f);

            // Gathering way is slow; losing it is slower still unless the anchor's holding.
            float target = _sail * top;
            bool speedingUp = Mathf.Abs(target) > Mathf.Abs(_speed) && target * _speed >= 0f;
            float seconds = speedingUp ? secondsToFullSpeed : anchored ? secondsToAnchorStop : secondsToCoastStop;
            _speed = Mathf.MoveTowards(_speed, target, top / Mathf.Max(seconds, 0.1f) * dt);

            // The rudder needs water flowing past it. Going astern, it steers the other way.
            float grip = Mathf.Clamp01(Mathf.Abs(_speed) / Mathf.Max(top * steerageSpeed, 0.01f));
            float wantedTurn = Steer * maxTurnRate * grip * (_speed < 0f ? -1f : 1f);
            _turnRate = Mathf.MoveTowards(_turnRate, wantedTurn, maxTurnRate / Mathf.Max(turnResponse, 0.05f) * dt);
            _buoyancy.Heading += _turnRate * dt;

            // Leans out of the turn, more the faster it's going.
            float heel = -_turnRate / Mathf.Max(maxTurnRate, 0.01f) * heelDegrees * Mathf.Clamp01(Mathf.Abs(_speed) / Mathf.Max(top, 0.01f));
            _buoyancy.Lean = Quaternion.AngleAxis(heel, _localBow);

            if (Mathf.Abs(_speed) < 0.001f)
            {
                _blockedReason = null;
                return;
            }

            Vector3 step = Bow * (_speed * dt);
            if (Blocked(transform.position + step, Mathf.Sign(_speed), out string reason))
            {
                _speed = 0f;
                _sail = 0f;
                if (reason != _blockedReason || Time.time - _blockedMessageTime > 4f)
                {
                    Hud.Error(reason);
                    _blockedMessageTime = Time.time;
                }
                _blockedReason = reason;
                return;
            }

            _blockedReason = null;
            transform.position += step;
        }

        /// <summary>Would the bow (or stern, going astern) be over water too shallow for the keel,
        /// or past the edge of the sea, with the ship's middle at <paramref name="position"/>?</summary>
        bool Blocked(Vector3 position, float direction, out string reason)
        {
            reason = null;
            Vector3 centre = position + (Centre - transform.position);
            Vector3 probe = centre + Bow * (direction * _halfLength * 0.9f);

            // Area only: the probe is inside the hull's own dry zone, where the water reports
            // "not wet" on purpose, and in a storm a trough can drop below any fixed depth.
            if (!WaterVolume.AnyCovers(probe))
            {
                reason = "The ship can't sail past the edge of the sea.";
                return true;
            }

            if (Seabed.HeightAt(probe, _water) > _water.SurfaceY - _keelDepth * 0.7f)
            {
                reason = direction > 0f
                    ? "The ship has run aground! Take in the sails (S) to back off."
                    : "The stern has run aground!";
                return true;
            }

            return false;
        }

        /// <summary>The ship's length along its bow and its keel's depth, from its renderers as
        /// they're placed, measured in its own frame.</summary>
        void Measure()
        {
            _measured = true;
            if (_localBow == Vector3.zero)
                GuessBow();

            var renderers = GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0)
                return;

            float minAlong = float.MaxValue, maxAlong = float.MinValue, minY = float.MaxValue;
            var bounds = new Bounds();
            bool first = true;
            foreach (var renderer in renderers)
            {
                if (renderer.GetComponentInParent<ShipLadder>() != null)
                    continue;
                var b = renderer.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var corner = new Vector3(i % 2 == 0 ? b.min.x : b.max.x, (i / 2) % 2 == 0 ? b.min.y : b.max.y,
                                             i / 4 == 0 ? b.min.z : b.max.z);
                    var local = transform.InverseTransformPoint(corner);
                    float along = Vector3.Dot(Vector3.Scale(local, transform.lossyScale), _localBow);
                    minAlong = Mathf.Min(minAlong, along);
                    maxAlong = Mathf.Max(maxAlong, along);
                    if (first) { bounds = new Bounds(local, Vector3.zero); first = false; }
                    else bounds.Encapsulate(local);
                }
                minY = Mathf.Min(minY, b.min.y);
            }

            _halfLength = (maxAlong - minAlong) * 0.5f;
            _localCentre = new Vector3(bounds.center.x, 0f, bounds.center.z);
            _keelDepth = Mathf.Max(_water.SurfaceY - minY, 1f);
        }

        void GuessBow()
        {
            var renderers = GetComponentsInChildren<Renderer>();
            var size = Vector3.zero;
            if (renderers.Length > 0)
            {
                var b = renderers[0].bounds;
                foreach (var renderer in renderers)
                    b.Encapsulate(renderer.bounds);
                size = transform.InverseTransformVector(b.size);
            }
            _localBow = Mathf.Abs(size.x) > Mathf.Abs(size.z) ? Vector3.right : Vector3.forward;
        }
    }
}
