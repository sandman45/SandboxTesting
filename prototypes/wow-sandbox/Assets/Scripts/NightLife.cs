using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace WowSandbox
{
    /// <summary>
    /// What the island does after dark, shared by WanderingNpc, NpcCombat and Vendor:
    /// - bedtime: everyone turns in a little after dusk and gets up around dawn, each at their
    ///   own moment (a per-NPC jitter) so they don't all drop at once;
    /// - the night watch: the humanoid nearest each world lantern stays up and patrols around
    ///   it, the rest sleep;
    /// - being seen: in the dark a guard spots you at half range, unless you're carrying a
    ///   lit torch or lantern, and a sleeper wakes when you come close — closer still unlit.
    /// No day and night cycle, no night.
    /// </summary>
    public static class NightLife
    {
        /// <summary>The earliest anyone goes to bed, and how much later the last one does (game hours).</summary>
        public const float Bedtime = 19.5f;
        /// <summary>The earliest anyone gets up.</summary>
        public const float Rise = 5.5f;
        public const float Stagger = 1f;

        /// <summary>A guard keeps watch at a lantern this close to its home.</summary>
        public const float GuardReach = 20f;
        /// <summary>How far a guard patrols from its lantern.</summary>
        public const float PatrolRadius = 5f;

        /// <summary>A sleeper wakes when you're this close, or <see cref="WakeRangeLit"/> carrying a light.</summary>
        public const float WakeRange = 3f;
        public const float WakeRangeLit = 6f;
        /// <summary>A guard's sight in the dark, as a share of its daytime aggro range, when you carry no light.</summary>
        public const float DarkSight = 0.5f;

        static readonly Dictionary<WanderingNpc, Vector3> Posts = new();
        static float _postsFound = float.NegativeInfinity;

        /// <summary>Bedtime for someone with this jitter (0..1): true from their bedtime until they get up.</summary>
        public static bool IsBedtime(float jitter)
        {
            var cycle = DayNightCycle.Active;
            if (cycle == null)
                return false;
            float hour = cycle.hour;
            return hour >= Bedtime + jitter * Stagger || hour < Rise + jitter * Stagger;
        }

        /// <summary>Dark enough that you're hard to see.</summary>
        static bool Dark => DayNightCycle.Active != null && DayNightCycle.Active.Daylight < 0.3f;

        /// <summary>A watcher's aggro range right now: halved in the dark, unless you carry a light.</summary>
        public static float SightRange(float range) => Dark && !HeldLight.IsLit ? range * DarkSight : range;

        /// <summary>How close you can get to a sleeper before it wakes.</summary>
        public static float WakeDistance => HeldLight.IsLit ? WakeRangeLit : WakeRange;

        /// <summary>
        /// Whether <paramref name="npc"/> keeps the night watch, and the spot by its lantern to
        /// patrol around. Worked out every so often rather than once, since NPCs die and spawn.
        /// </summary>
        public static bool IsGuard(WanderingNpc npc, out Vector3 post)
        {
            if (Time.time - _postsFound > 20f)
                FindPosts();
            return Posts.TryGetValue(npc, out post);
        }

        /// <summary>For each lantern on the island, the humanoid whose home is nearest it within GuardReach.</summary>
        static void FindPosts()
        {
            _postsFound = Time.time;
            Posts.Clear();
            var people = new List<WanderingNpc>();
            foreach (var npc in Object.FindObjectsByType<WanderingNpc>(FindObjectsSortMode.None))
            {
                if (npc.IsAnimal || npc.GetComponent<NpcCombat>() == null)
                    continue;
                var health = npc.GetComponent<Health>();
                if (health == null || !health.IsDead)
                    people.Add(npc);
            }
            if (people.Count == 0)
                return;

            foreach (var light in Object.FindObjectsByType<NightLight>(FindObjectsSortMode.None))
            {
                // Lights in someone's hand don't count, nor ones with no ground beside them (the ship).
                if (light.held || !NavMesh.SamplePosition(light.transform.position, out var ground, 4f, NavMesh.AllAreas))
                    continue;

                WanderingNpc nearest = null;
                float best = GuardReach * GuardReach;
                foreach (var npc in people)
                {
                    float distance = (npc.Home - ground.position).sqrMagnitude;
                    if (distance < best)
                    {
                        best = distance;
                        nearest = npc;
                    }
                }
                if (nearest != null && !Posts.ContainsKey(nearest))
                    Posts[nearest] = ground.position;
            }
        }
    }
}
