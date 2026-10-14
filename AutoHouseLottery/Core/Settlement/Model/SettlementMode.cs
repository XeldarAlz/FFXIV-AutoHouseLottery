namespace AutoHouseLottery.Core.Settlement.Model;

// Inspect reads the result and closes it. Refund inspects and, for a refundable result, collects it. Claim inspects
// and, for a win, claims the plot; it only ever starts from the player's own click.
internal enum SettlementMode : byte { Inspect, Refund, Claim }

// Who asked: the player on My lottery, the scheduler, an entry run settling an earlier entry first, or a dev command.
internal enum SettlementOrigin : byte { Player, Scheduler, EntryRun, Command }

// One piece of settlement work, which is also its dedupe key: one receipt, one kind of action.
internal readonly record struct SettlementWork(Guid ReceiptId, SettlementMode Mode);
