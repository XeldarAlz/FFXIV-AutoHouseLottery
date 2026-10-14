using AutoHouseLottery.Core.Game.Model;

namespace AutoHouseLottery.Core.Game.Dialogs;

// Every housing click starts forbidden. A caller permits exactly the actions it means to take for the length of one
// using block, so a click can never happen on a path nobody opened for it. Framework thread only.
internal static class NativeActionGate
{
    private static NativeAction permitted;

    public static bool IsPermitted(NativeAction action) => action != NativeAction.None && (permitted & action) == action;

    public static Permit Allow(NativeAction actions)
    {
        permitted |= actions;
        return new Permit(actions);
    }

    internal readonly ref struct Permit(NativeAction actions)
    {
        public void Dispose() => permitted &= ~actions;
    }
}
