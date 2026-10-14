namespace AutoHouseLottery.Core.Game.Model;

// Every housing click starts forbidden. A caller permits exactly the actions it means to take for the length of one
// using block, so a click can never happen on a path nobody opened for it. The permission belongs to the thread that
// granted it: a press attempted from any other thread sees nothing permitted.
internal static class NativeActionGate
{
    [ThreadStatic]
    private static NativeAction permitted;

    public static bool IsPermitted(NativeAction action) => action != NativeAction.None && (permitted & action) == action;

    public static Permit Allow(NativeAction actions)
    {
        var previous = permitted;
        permitted |= actions;
        return new Permit(previous);
    }

    // Restores what was permitted before, so nested permits unwind in order.
    internal readonly ref struct Permit(NativeAction previous)
    {
        public void Dispose() => permitted = previous;
    }
}
