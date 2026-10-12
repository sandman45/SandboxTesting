using System.Collections;
using UnityEngine;

namespace WowSandbox
{
    /// <summary>
    /// The Ancient Leviathan: hidden until something calls on it. RiseAndStrike stages it
    /// where you can see it — it breaches in front of you, towering out of the sea facing
    /// you, holds there with a roar, then arcs over and comes down on you jaws first.
    /// DeepSeaDanger then drags it (and you) under. It has no Health — there is no fighting
    /// it, and its export has no Death clip to play anyway.
    ///
    /// Root forward is the creature's facing and the root sits at the body's centre (both
    /// set up by WoW Sandbox → Setup Deep Sea Danger), so pointing forward straight up puts
    /// the head on top.
    /// </summary>
    public class Leviathan : MonoBehaviour
    {
        [Tooltip("Seconds to come up out of the depths.")]
        public float riseSeconds = 3f;
        [Tooltip("Seconds it towers over you, roaring, before it strikes.")]
        public float holdSeconds = 1.5f;
        [Tooltip("Seconds for the lunge down onto you.")]
        public float strikeSeconds = 0.7f;
        [Tooltip("How much of its length shows above the water once it's up, 0-1.")]
        [Range(0.1f, 0.9f)] public float breach = 0.3f;
        [Tooltip("Extra gap between you and where it surfaces, beyond its own reach.")]
        public float surfaceGap = 10f;
        [Tooltip("How far below its breached position it starts, beyond its own length.")]
        public float riseFrom = 40f;

        [Header("Storm appearance")]
        [Tooltip("Seconds to rise out of the sea at the storm's peak.")]
        public float emergeSeconds = 8f;
        [Tooltip("Seconds to sink back under as the storm clears.")]
        public float sinkSeconds = 7f;
        [Tooltip("How far below the surface the middle of its body sits while it looms. 0 = half " +
                 "in, half out; more sinks it lower, negative lifts it higher.")]
        public float loomDepth = 0f;
        [Tooltip("Seconds between its roars and spell casts while it's up (random in this range).")]
        public Vector2 castInterval = new Vector2(7f, 15f);
        [Tooltip("How far it ducks down into the water during each roar or cast.")]
        public float castDip = 4f;
        [Tooltip("Seconds to dip down and come back up again for a roar or cast.")]
        public float castDipSeconds = 3f;
        [Tooltip("How far it sways round to either side while it looms, in degrees.")]
        public float swayDegrees = 20f;
        [Tooltip("Seconds for one full sway, side to side and back.")]
        public float swaySeconds = 40f;

        /// <summary>The leviathan while it's up out at sea in a storm, for the map; otherwise null.</summary>
        public static Leviathan Looming { get; private set; }

        /// <summary>Called on each roar or cast while it looms — StormAudio answers with a rumble.</summary>
        public static event System.Action<Leviathan> Roared;

        /// <summary>Already out — rising, looming, or busy with a swimmer. Hidden when free.</summary>
        public bool InUse => gameObject.activeSelf;

        static readonly int SpeedHash = Animator.StringToHash("Speed");
        static readonly int AttackHash = Animator.StringToHash("Attack");
        static readonly int SubmergeHash = Animator.StringToHash("Submerge");

        Animator _animator;
        float _halfLength = -1f;

        /// <summary>
        /// Surfaces ahead of <paramref name="victim"/> along <paramref name="viewForward"/>
        /// (the camera's facing, so it's in shot), rises, roars, and lunges onto them.
        /// </summary>
        public IEnumerator RiseAndStrike(Vector3 victim, Vector3 viewForward, float surfaceY)
        {
            gameObject.SetActive(true);
            _animator = GetComponentInChildren<Animator>();
            MeasureOnce();

            viewForward.y = 0f;
            viewForward = viewForward.sqrMagnitude > 0.01f ? viewForward.normalized : Vector3.forward;

            // Far enough out that its whole lunge lands on you, plus a gap so it's in view.
            Vector3 spot = victim + viewForward * (_halfLength * 0.8f + surfaceGap);
            spot.y = surfaceY;

            // Head up, back turned away from you — it rises facing you.
            var upright = Quaternion.LookRotation(Vector3.up, viewForward);
            float aboveWater = _halfLength * 2f * breach;
            Vector3 breached = spot + Vector3.up * (aboveWater - _halfLength);
            Vector3 deep = breached - Vector3.up * (_halfLength * 2f + riseFrom);

            transform.SetPositionAndRotation(deep, upright);
            SetSpeed(2f);
            for (float t = 0f; t < riseSeconds; t += Time.deltaTime)
            {
                // Smoothstep: gathers speed in the dark, then slows as it towers up.
                float k = t / riseSeconds;
                transform.position = Vector3.Lerp(deep, breached, k * k * (3f - 2f * k));
                yield return null;
            }
            transform.position = breached;

            SetSpeed(0f);
            Attack(); // the roar
            yield return new WaitForSeconds(holdSeconds);

            // Arc over and down: jaws (the front of the body) end on you.
            Vector3 lungeDirection = (victim - spot).normalized;
            var striking = Quaternion.LookRotation(lungeDirection, Vector3.up);
            Vector3 struck = victim - lungeDirection * (_halfLength * 0.9f);
            Attack();
            for (float t = 0f; t < strikeSeconds; t += Time.deltaTime)
            {
                float k = t / strikeSeconds;
                k *= k; // falls faster and faster
                transform.SetPositionAndRotation(Vector3.Lerp(breached, struck, k),
                    Quaternion.Slerp(upright, striking, k));
                yield return null;
            }
            transform.SetPositionAndRotation(struck, striking);
        }

        /// <summary>
        /// The storm spectacle: surfaces at <paramref name="spot"/> lying level, half in the water,
        /// facing <paramref name="watcher"/>, and stays there swaying, roaring and casting for as long as
        /// <paramref name="stayUp"/> holds — then sinks back under and hides. Nothing to fight:
        /// it never comes any closer.
        /// </summary>
        public IEnumerator Loom(Vector3 spot, Vector3 watcher, System.Func<bool> stayUp)
        {
            gameObject.SetActive(true);
            _animator = GetComponentInChildren<Animator>();
            MeasureOnce();
            Looming = this;

            Vector3 away = spot - watcher;
            away.y = 0f;
            away = away.sqrMagnitude > 0.01f ? away.normalized : Vector3.forward;
            // Level and back up, as it swims, head toward the watcher — so its roars and casts,
            // which are animated for a creature lying level, aim at them rather than the sky.
            var facing = Quaternion.LookRotation(-away, Vector3.up);

            // The root is the body's centre, so on the surface means half in, half out.
            Vector3 breached = spot + Vector3.down * loomDepth;
            Vector3 deep = breached - Vector3.up * (riseFrom + _halfLength * 0.3f);

            transform.SetPositionAndRotation(deep, facing);
            SetSpeed(1f);
            float t = 0f;
            for (; t < emergeSeconds && stayUp(); t += Time.deltaTime)
            {
                float k = t / emergeSeconds;
                transform.position = Vector3.Lerp(deep, breached, k * k * (3f - 2f * k));
                yield return null;
            }

            SetSpeed(0f);
            float castStart = -100f;
            if (stayUp())
            {
                Cast();
                castStart = Time.time;
            }

            float nextCast = Time.time + Random.Range(castInterval.x, castInterval.y);
            float swayStart = Time.time;
            while (stayUp())
            {
                float phase = (Time.time - swayStart) / Mathf.Max(swaySeconds, 1f) * Mathf.PI * 2f;
                // Down and back up again over the dip, smoothly at both ends.
                float dip = Mathf.Clamp01((Time.time - castStart) / Mathf.Max(castDipSeconds, 0.1f));
                float duck = castDip * Mathf.Sin(dip * Mathf.PI);
                transform.SetPositionAndRotation(
                    breached + Vector3.up * (Mathf.Sin(phase * 3f) * 2f - duck),
                    Quaternion.AngleAxis(Mathf.Sin(phase) * swayDegrees, Vector3.up) * facing);

                if (Time.time >= nextCast)
                {
                    Cast();
                    castStart = Time.time;
                    nextCast = Time.time + Random.Range(castInterval.x, castInterval.y);
                }
                yield return null;
            }

            // Back down from wherever it got to — the storm may have broken mid-rise.
            if (HasParameter(SubmergeHash))
                _animator.SetTrigger(SubmergeHash);
            Vector3 from = transform.position;
            for (t = 0f; t < sinkSeconds; t += Time.deltaTime)
            {
                float k = t / sinkSeconds;
                transform.position = Vector3.Lerp(from, deep, k * k); // slow to start, then gone
                yield return null;
            }

            Hide();
        }

        /// <summary>A roar or spell: one of its Cast clips at random, or its attack if it has none.</summary>
        void Cast()
        {
            Roared?.Invoke(this);
            if (_animator == null || _animator.runtimeAnimatorController == null)
                return;

            int casts = 0;
            while (HasParameter(Animator.StringToHash("Cast" + casts)))
                casts++;
            if (casts > 0)
                _animator.SetTrigger("Cast" + Random.Range(0, casts));
            else
                Attack();
        }

        void Attack()
        {
            if (HasParameter(AttackHash))
                _animator.SetTrigger(AttackHash);
        }

        public void Hide() => gameObject.SetActive(false);

        void OnDisable()
        {
            if (Looming == this)
                Looming = null;
        }

        void MeasureOnce()
        {
            if (_halfLength > 0f)
                return;

            var saved = transform.rotation;
            transform.rotation = Quaternion.identity;
            var renderers = GetComponentsInChildren<Renderer>();
            if (renderers.Length > 0)
            {
                var bounds = renderers[0].bounds;
                foreach (var renderer in renderers)
                    bounds.Encapsulate(renderer.bounds);
                _halfLength = bounds.extents.z;
            }
            else
            {
                _halfLength = 20f;
            }
            transform.rotation = saved;
        }

        void SetSpeed(float speed)
        {
            if (_animator != null && _animator.runtimeAnimatorController != null)
                _animator.SetFloat(SpeedHash, speed);
        }

        bool HasParameter(int hash)
        {
            if (_animator == null || _animator.runtimeAnimatorController == null)
                return false;
            foreach (var parameter in _animator.parameters)
            {
                if (parameter.nameHash == hash)
                    return true;
            }
            return false;
        }
    }
}
