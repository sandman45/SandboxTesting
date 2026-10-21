using UnityEngine;
using UnityEngine.AI;

namespace WowSandbox
{
    /// <summary>
    /// The wandering behaviour now lives in <see cref="WanderingNpc"/> so warriors and
    /// thieves can use it too. This subclass is kept because the chickens already in the
    /// scene serialise a reference to this component by name — deleting it would leave
    /// them with a missing script. Its inspector fields still work; they're inherited.
    ///
    /// At night a chicken roosts with its flock: they huddle at the middle of their homes.
    /// </summary>
    public class ChickenWanderer : WanderingNpc
    {
        /// <summary>Chickens whose homes are this close roost together.</summary>
        const float FlockRange = 15f;

        Vector3? _roost;

        protected override bool FearsFire => true;

        protected override Vector3 Bed => _roost ??= FindRoost();

        /// <summary>A spot a little off the middle of the flock's homes, so they bunch rather than stack.</summary>
        Vector3 FindRoost()
        {
            Vector3 sum = Vector3.zero;
            int count = 0;
            foreach (var chicken in FindObjectsByType<ChickenWanderer>(FindObjectsSortMode.None))
            {
                if ((chicken.Home - Home).sqrMagnitude > FlockRange * FlockRange)
                    continue;
                sum += chicken.Home;
                count++;
            }
            Vector3 middle = count > 0 ? sum / count : Home;

            for (int attempt = 0; attempt < 6; attempt++)
            {
                var offset = Random.insideUnitCircle * 1.5f;
                if (NavMesh.SamplePosition(middle + new Vector3(offset.x, 0f, offset.y), out var hit, 2f, NavMesh.AllAreas))
                    return hit.position;
            }
            return Home;
        }
    }
}
