namespace AutoHouseLottery.Core.Settlement.Model;

// Inspect reads the result and closes it. Refund inspects and, for a refundable result, collects it. Claim inspects
// and, for a win, claims the plot; it only ever starts from the player's own click.
internal enum SettlementMode : byte { Inspect, Refund, Claim }

// Who asked: the player on My lottery, the scheduler, an entry run settling an earlier entry first, or a dev command.
internal enum SettlementOrigin : byte { Player, Scheduler, EntryRun, Command }

// One piece of settlement work, which is also its dedupe key: one item, one kind of action. Id is a receipt, or an
// earlier cycle's entry attempt still in question when the result check is about one (ResultChecks).
internal readonly record struct SettlementWork(Guid Id, SettlementMode Mode);
