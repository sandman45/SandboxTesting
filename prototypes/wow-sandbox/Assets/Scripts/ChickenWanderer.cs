namespace WowSandbox
{
    /// <summary>
    /// The wandering behaviour now lives in <see cref="WanderingNpc"/> so warriors and
    /// thieves can use it too. This subclass is kept because the chickens already in the
    /// scene serialise a reference to this component by name — deleting it would leave
    /// them with a missing script. Its inspector fields still work; they're inherited.
    /// </summary>
    public class ChickenWanderer : WanderingNpc
    {
    }
}
