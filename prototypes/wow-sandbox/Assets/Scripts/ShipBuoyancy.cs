using UnityEngine;

namespace WowSandbox
{
    /// <summary>
    /// Floats a ship on WaterWaves: each frame it samples the wave height at the centre, bow,
    /// stern and both beams, rises and falls with the average, and pitches and rolls with the
    /// differences — eased, so a big hull moves with some weight instead of twitching with
    /// every ripple. A long hull also averages out the short waves by itself, which is what
    /// keeps a galleon steady in a chop that would toss a rowing boat.
    ///
    /// Kinematic, not physics: it reads the same HeightAt the swim check uses, so the hull
    /// sits where the drawn water is. Play mode only — in the editor the ship stays where you
    /// placed it, and that placement defines its waterline: however deep it sits in calm water
    /// when Play starts is the draft it keeps.
    ///
    /// Adds a kinematic Rigidbody so its MeshColliders count as moving rather than static,
    /// and so WowCharacterController can recognise the deck as a platform to ride.
    /// </summary>
    [DisallowMultipleComponent]
    // After WaterWaves (-100) has advanced the waves, before the player (0) moves on the deck.
    [DefaultExecutionOrder(-50)]
    public class ShipBuoyancy : MonoBehaviour
    {
        [Tooltip("How far from the centre to sample along the ship's local X and Z. Taken from " +
                 "the WaterDryZone's hull outline when there is one; these are the fallback, " +
                 "in local units.")]
        public Vector2 sampleReach = new Vector2(10f, 35f);

        [Tooltip("How much of the waves' rise and fall the ship follows. 1 = all of it.")]
        [Range(0f, 1.5f)] public float bobAmount = 0.8f;
        [Tooltip("How much of the waves' slope the ship tilts with. 1 = all of it.")]
        [Range(0f, 2f)] public float tiltAmount = 1f;
        [Tooltip("Never pitch or roll further than this, in degrees.")]
        public float maxTilt = 12f;

        [Tooltip("Seconds to catch up with the waves. Bigger = heavier, lazier ship.")]
        public float responseTime = 0.8f;

        WaterVolume _water;
        WaterWaves _waves;
        float _draft;           // ship origin height above calm sea level, from the editor placement
        Quaternion _yaw;        // the heading you placed it at; buoyancy only adds pitch and roll
        float _velocityY;
        Vector2 _reach;         // half-extents to sample along local X (x) and Z (y)
        Vector3 _hullCentre;    // the hull's middle, which needn't be the model's origin

        void Start()
        {
            _water = FindFirstObjectByType<WaterVolume>();
            _waves = _water != null ? _water.GetComponent<WaterWaves>() : null;
            if (_water == null)
            {
                Debug.LogWarning("[ShipBuoyancy] No water in the scene to float on.", this);
                enabled = false;
                return;
            }

            // Catch it if the static flags came back (a prefab revert, say): a batched hull
            // would stay put while its colliders and the player float away from it.
            foreach (var renderer in GetComponentsInChildren<Renderer>())
            {
                if (renderer.isPartOfStaticBatch)
                {
                    Debug.LogError($"[ShipBuoyancy] {renderer.name} is static-batched, so the visible ship " +
                                   "won't move. Exit Play, select the ship (the Static flags clear " +
                                   "themselves), save, and Play again.", renderer);
                    break;
                }
            }

            _draft = transform.position.y - _water.SurfaceY;
            _yaw = Quaternion.Euler(0f, transform.eulerAngles.y, 0f);
            ReadHull();

            // Moving colliders belong on a kinematic body; it also marks the deck as a
            // platform for the character controller.
            var body = GetComponent<Rigidbody>();
            if (body == null)
                body = gameObject.AddComponent<Rigidbody>();
            body.isKinematic = true;
            body.interpolation = RigidbodyInterpolation.None;
        }

        void Update()
        {
            // Which local axis is the ship's length doesn't matter: tilt about each axis
            // follows the wave slope along the other.
            float centre = Height(Vector3.zero);
            float plusZ = Height(new Vector3(0f, 0f, _reach.y));
            float minusZ = Height(new Vector3(0f, 0f, -_reach.y));
            float plusX = Height(new Vector3(_reach.x, 0f, 0f));
            float minusX = Height(new Vector3(-_reach.x, 0f, 0f));

            float average = (centre * 2f + plusZ + minusZ + plusX + minusX) / 6f;
            float targetY = _water.SurfaceY + _draft + average * bobAmount;

            // Positive X rotation lowers +Z; positive Z rotation lifts +X — hence the signs.
            Vector3 scale = transform.lossyScale;
            float tiltX = -Mathf.Atan2(plusZ - minusZ, _reach.y * 2f * scale.z) * Mathf.Rad2Deg * tiltAmount;
            float tiltZ = Mathf.Atan2(plusX - minusX, _reach.x * 2f * scale.x) * Mathf.Rad2Deg * tiltAmount;
            tiltX = Mathf.Clamp(tiltX, -maxTilt, maxTilt);
            tiltZ = Mathf.Clamp(tiltZ, -maxTilt, maxTilt);

            Vector3 position = transform.position;
            position.y = Mathf.SmoothDamp(position.y, targetY, ref _velocityY, responseTime);

            Quaternion targetRotation = _yaw * Quaternion.Euler(tiltX, 0f, tiltZ);
            float blend = 1f - Mathf.Exp(-Time.deltaTime / Mathf.Max(responseTime, 0.01f));
            Quaternion rotation = Quaternion.Slerp(transform.rotation, targetRotation, blend);

            transform.SetPositionAndRotation(position, rotation);

            // Physics doesn't see a transform change until the next simulation step. The
            // player moves this same frame, so push the new deck position through now or
            // they'd be colliding with where the ship was.
            Physics.SyncTransforms();
        }

        /// <summary>Wave height under a point given in the ship's level, unrotated frame.</summary>
        float Height(Vector3 localOffset)
        {
            // Local units scaled to the world, so a ship placed at Scale 0.6 samples its own
            // smaller hull rather than water beyond the bow.
            Vector3 world = transform.position + _yaw * Vector3.Scale(_hullCentre + localOffset, transform.lossyScale);
            return _waves != null && _waves.isActiveAndEnabled ? _waves.HeightAt(world.x, world.z) : 0f;
        }

        /// <summary>
        /// Where to sample, from the dry zone's waterline outline when there is one: the
        /// hull's centre, and its half-extents along local X and Z pulled in a little so the
        /// samples land on water the hull actually sits in.
        /// </summary>
        void ReadHull()
        {
            _reach = sampleReach;
            _hullCentre = Vector3.zero;

            var zone = GetComponentInChildren<WaterDryZone>();
            if (zone == null || !zone.HasHullProfile || zone.transform.parent != transform)
                return;

            // The slice nearest the waterline: the profile runs from the keel up, so that's
            // wherever calm sea level crosses it, not the middle.
            float waterLocalY = zone.transform.InverseTransformPoint(
                new Vector3(zone.transform.position.x, _water.SurfaceY, zone.transform.position.z)).y;
            int level = Mathf.Clamp(
                Mathf.RoundToInt(Mathf.InverseLerp(zone.hullBottom, zone.hullTop, waterLocalY) * (WaterDryZone.HullLevels - 1)),
                0, WaterDryZone.HullLevels - 1);
            float Radius(int sample) => zone.hullRadii[level * WaterDryZone.HullSamples + sample];

            // Samples run from local +X towards +Z, a quarter turn apart for +X, +Z, -X, -Z.
            int quarter = WaterDryZone.HullSamples / 4;
            float alongX = (Radius(0) + Radius(quarter * 2)) * 0.5f;
            float alongZ = (Radius(quarter) + Radius(quarter * 3)) * 0.5f;

            _reach = new Vector2(alongX, alongZ) * 0.8f;
            Vector3 centre = zone.transform.localPosition + new Vector3(zone.hullCentre.x, 0f, zone.hullCentre.y);
            _hullCentre = new Vector3(centre.x, 0f, centre.z);
        }

        void OnValidate()
        {
            maxTilt = Mathf.Max(maxTilt, 0f);
#if UNITY_EDITOR
            // Deferred: changing static flags from inside OnValidate isn't allowed.
            UnityEditor.EditorApplication.delayCall += ClearStaticFlags;
#endif
        }

#if UNITY_EDITOR
        void Reset() => ClearStaticFlags();

        /// <summary>
        /// wow.unity marks every prefab it builds — the ship and each doodad on it — Static.
        /// Unity then static-batches those meshes when Play starts, baking them in place: the
        /// colliders (and anyone standing on them) follow this component while the hull you
        /// can see stays exactly where it was. It has to be cleared in the editor, because
        /// batching happens before any script runs. Static only ever helps things that never
        /// move, and this ship moves.
        /// </summary>
        void ClearStaticFlags()
        {
            if (this == null)
                return;

            int cleared = 0;
            foreach (var child in GetComponentsInChildren<Transform>(true))
            {
                if (UnityEditor.GameObjectUtility.GetStaticEditorFlags(child.gameObject) == 0)
                    continue;

                UnityEditor.Undo.RecordObject(child.gameObject, "Clear Static on Floating Ship");
                UnityEditor.GameObjectUtility.SetStaticEditorFlags(child.gameObject, 0);
                cleared++;
            }

            if (cleared > 0)
            {
                UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(gameObject.scene);
                Debug.Log($"[ShipBuoyancy] Cleared Static on {cleared} object(s) under {name} — a static " +
                          "mesh is batched in place at Play and can't float. Save the scene.", this);
            }
        }
#endif
    }
}
