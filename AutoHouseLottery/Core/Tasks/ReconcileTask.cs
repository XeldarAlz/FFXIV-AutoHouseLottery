using System.Threading.Tasks;
using AutoHouseLottery.Core.Game;
using AutoHouseLottery.Core.Game.Character;
using AutoHouseLottery.Core.Game.Lottery;
using AutoHouseLottery.Core.Game.Model;
using AutoHouseLottery.Core.Integration;
using AutoHouseLottery.Core.Integration.Model;
using AutoHouseLottery.Core.Persistence.Model;
using AutoHouseLottery.Core.Tasks.Model;

namespace AutoHouseLottery.Core.Tasks;

internal enum ReconcileOutcome : byte { NotRun, NothingPending, NoCharacter, NotHomeWorld, NotRead, Settled, StillUncertain }

internal readonly record struct ReconcileReport(ReconcileOutcome Outcome, int Confirmed, int Rejected, int Remaining, DateTime AtUtc);

// Reads the logged-in character's uncertain entry attempts back against a fresh lottery reading. Nothing is pressed:
// an attempt is confirmed or released only by what the reading shows, and everything else stays in question.
internal sealed class ReconcileTask(HousingContext housing) : AutoCommon
{
    private const int TimersPassWatchdogMs = 75_000;

    public ReconcileReport Report { get; private set; }

    protected override async Task Execute()
    {
        Status = "Checking whether earlier entries completed";
        Report = new ReconcileReport(ReconcileOutcome.NotRun, 0, 0, 0, DateTime.UtcNow);
        if (CharacterContextReader.Read() is not { } context || housing.Journal is not { } journal)
        {
            Finish(ReconcileOutcome.NoCharacter, 0, 0, 0);
            return;
        }

        var pending = Pending(journal.Snapshot(), context.ContentId);
        if (pending.Count == 0)
        {
            Finish(ReconcileOutcome.NothingPending, 0, 0, 0);
            return;
        }

        if (!context.OnHomeWorld)
        {
            Finish(ReconcileOutcome.NotHomeWorld, 0, 0, pending.Count);
            return;
        }

        var pass = new LotteryTimersPass();
        await RunCancellable(pass, TimersPassWatchdogMs, "reconcile-lottery-reading");
        if (pass.Outcome != LotteryPassOutcome.Loaded || pass.Snapshot.ContentId != context.ContentId)
        {
            Finish(ReconcileOutcome.NotRead, 0, 0, pending.Count);
            return;
        }

        var reading = pass.Snapshot;
        var position = housing.CurrentPosition(reading.ReadAtUtc);
        var confirmed = 0;
        var rejected = 0;
        for (var index = 0; index < pending.Count; index++)
        {
            var attempt = pending[index];
            bool? namesPlot = reading.Entry is { } entry ? ReadingAddress.Names(entry, attempt.Plot, DistrictNames.Of) : null;
            var inPhase = reading.Entry is { } shown && ParticipationRules.SamePhase(shown.Phase, position.Phase);
            var verdict = ReconciliationRules.Decide(new ReconciliationFacts(attempt, position, reading, namesPlot, inPhase));
            var written = verdict switch
            {
                ReconciliationVerdict.Confirmed => await Task.Run(() => ConfirmFromReading(journal, attempt, reading)),
                ReconciliationVerdict.Rejected => await Task.Run(() => journal.Reject(attempt.AttemptId, DateTime.UtcNow)),
                _ => JournalWrite.AlreadyRecorded,
            };
            await NextFrame();
            RunLog.Info($"Reconciliation: attempt {attempt.AttemptId} for {attempt.Plot} in cycle {attempt.Cycle} ({attempt.State}) is {verdict}; journal {written}. "
                      + $"Reading: {LotteryReader.Describe(reading)}, names the plot {namesPlot?.ToString() ?? "unknown"}.");
            if (written is not (JournalWrite.Written or JournalWrite.AlreadyRecorded))
            {
                continue;
            }

            confirmed += verdict == ReconciliationVerdict.Confirmed ? 1 : 0;
            rejected += verdict == ReconciliationVerdict.Rejected ? 1 : 0;
        }

        await housing.ImportFromReading(reading, context.ContentId, context.HomeWorldId);
        var remaining = pending.Count - confirmed - rejected;
        Finish(remaining == 0 ? ReconcileOutcome.Settled : ReconcileOutcome.StillUncertain, confirmed, rejected, remaining);
    }

    private static List<ActionAttempt> Pending(in JournalSnapshot snapshot, ulong contentId)
    {
        var pending = new List<ActionAttempt>();
        var attempts = snapshot.Attempts;
        for (var index = 0; index < attempts.Length; index++)
        {
            var attempt = attempts[index];
            if (attempt.Kind == ActionKind.Entry
                && attempt.CharacterContentId == contentId
                && attempt.State is AttemptState.Prepared or AttemptState.Dispatched or AttemptState.NeedsReconciliation)
            {
                pending.Add(attempt);
            }
        }

        return pending;
    }

    // The deposit is the amount the confirmation showed when the intent was written; the time of entry is not known.
    private static JournalWrite ConfirmFromReading(Persistence.ActionJournal journal, ActionAttempt attempt, LotterySnapshot reading)
    {
        if (attempt.State == AttemptState.Prepared)
        {
            journal.RequireReconciliation(attempt.AttemptId, DateTime.UtcNow);
        }

        var number = reading.Entry?.LotteryNumber;
        var receipt = new EntryReceipt
        {
            ReceiptId = Guid.NewGuid(),
            CharacterContentId = attempt.CharacterContentId,
            HomeWorldId = attempt.HomeWorldId,
            Cycle = attempt.Cycle,
            Plot = attempt.Plot,
            Buyer = attempt.Buyer,
            Evidence = ReceiptEvidence.Reconciled,
            EntryNumber = number is { } value && value <= int.MaxValue ? (int)value : null,
            Deposit = attempt.Amount,
        };
        return journal.Confirm(attempt.AttemptId, receipt, DateTime.UtcNow);
    }

    private void Finish(ReconcileOutcome outcome, int confirmed, int rejected, int remaining)
    {
        Report = new ReconcileReport(outcome, confirmed, rejected, remaining, DateTime.UtcNow);
        RunLog.Info($"Reconciliation: {outcome}, {confirmed} confirmed, {rejected} released, {remaining} still in question.");
    }
}
