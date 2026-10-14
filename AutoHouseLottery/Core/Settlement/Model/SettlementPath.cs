using AutoHouseLottery.Core.Persistence.Model;

namespace AutoHouseLottery.Core.Settlement.Model;

internal static class SettlementPath
{
    public const int Unreachable = -1;

    // Longer than any walk the transitions allow, so a buffer of this size always fits one.
    public const int MaximumSteps = 8;

    private const int StateCount = (int)SettlementState.NeedsReconciliation + 1;
    private const sbyte NotVisited = -1;

    // The shortest walk of legal transitions from one recorded state to another, written into steps without the start;
    // the number of steps, 0 when already there, Unreachable when the journal's transitions do not lead there. So an
    // inspection can record its outcome through the states the journal requires on the way. Only the inspection states,
    // and a forfeited win's refund on the way to a settled state, may be passed through: a walk never invents a press
    // (RefundPending, ClaimPending) or a question about one.
    public static int Plan(SettlementState from, SettlementState to, Span<SettlementState> steps)
    {
        if (from == to)
        {
            return 0;
        }

        Span<sbyte> previous = stackalloc sbyte[StateCount];
        Span<byte> queue = stackalloc byte[StateCount];
        previous.Fill(NotVisited);
        previous[(int)from] = (sbyte)from;
        var head = 0;
        var tail = 0;
        queue[tail++] = (byte)from;
        while (head < tail)
        {
            var current = (SettlementState)queue[head++];
            for (var nextIndex = 0; nextIndex < StateCount; nextIndex++)
            {
                var next = (SettlementState)nextIndex;
                if (previous[nextIndex] != NotVisited || !SettlementTransitions.IsLegal(current, next))
                {
                    continue;
                }

                if (next != to && !IsWaypoint(next, to))
                {
                    continue;
                }

                previous[nextIndex] = (sbyte)current;
                if (next == to)
                {
                    return Unwind(from, to, previous, steps);
                }

                queue[tail++] = (byte)next;
            }
        }

        return Unreachable;
    }

    // A forfeited win's refund is passed through only on the way to a settled state, never to a press.
    private static bool IsWaypoint(SettlementState state, SettlementState to)
        => state is SettlementState.ResultsPending or SettlementState.InspectingResult
        || (state == SettlementState.ForfeitRefundDue && SettlementTransitions.IsSettled(to));

    private static int Unwind(SettlementState from, SettlementState to, ReadOnlySpan<sbyte> previous, Span<SettlementState> steps)
    {
        var length = 0;
        for (var state = to; state != from; state = (SettlementState)previous[(int)state])
        {
            length++;
        }

        if (length > steps.Length)
        {
            return Unreachable;
        }

        var position = length;
        for (var state = to; state != from; state = (SettlementState)previous[(int)state])
        {
            steps[--position] = state;
        }

        return length;
    }
}
