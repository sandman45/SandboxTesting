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

        static readonly int SpeedHash = Animator.StringToHash("Speed");
        static readonly int AttackHash = Animator.StringToHash("Attack");

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

        void Attack()
        {
            if (_animator != null && HasAttack())
                _animator.SetTrigger(AttackHash);
        }

        public void Hide() => gameObject.SetActive(false);

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

        bool HasAttack()
        {
            foreach (var parameter in _animator.parameters)
            {
                if (parameter.nameHash == AttackHash)
                    return true;
            }
            return false;
        }
    }
}
