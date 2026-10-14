using System.Threading.Tasks;
using AutoHouseLottery.Core.Game.Model;
using AutoHouseLottery.Core.Persistence;
using AutoHouseLottery.Core.Persistence.Model;

namespace AutoHouseLottery.Core.Tasks.Model;

internal enum SubmissionOutcome : byte
{
    // The gate is closed: nothing was written and nothing pressed.
    GateClosed,
    // The journal refused the intent (slot taken, invalid); nothing pressed.
    IntentRefused,
    // The journal could not write the intent; nothing pressed.
    JournalFault,
    // Confirm was refused before it was sent; the intent is recorded as rejected.
    PressRefused,
    // Confirm was sent and the dispatch is on disk.
    Dispatched,
    // Confirm was sent but the dispatch could not be recorded; the intent on disk already counts as possibly submitted.
    DispatchUnrecorded,
}

// The irreversible part of an entry in its only allowed order: the gate, the durable intent, one press, the dispatch.
// Disk work runs off the game thread; returnToGame brings the caller back to it before the press.
internal sealed class EntrySubmission(ActionJournal journal)
{
    public async Task<SubmissionOutcome> Submit(ActionAttempt intent, bool gateOpen, Func<Task> returnToGame, Func<DialogActionResult> pressConfirm, Func<DateTime> utcNow)
    {
        if (!gateOpen)
        {
            return SubmissionOutcome.GateClosed;
        }

        var prepared = await Task.Run(() => journal.Prepare(intent));
        if (prepared != JournalWrite.Written)
        {
            return prepared is JournalWrite.WriteFailed or JournalWrite.Faulted ? SubmissionOutcome.JournalFault : SubmissionOutcome.IntentRefused;
        }

        await returnToGame();
        var pressed = pressConfirm();
        if (pressed != DialogActionResult.Pressed)
        {
            await Task.Run(() => journal.Reject(intent.AttemptId, utcNow()));
            return SubmissionOutcome.PressRefused;
        }

        var dispatched = await Task.Run(() => journal.RecordDispatch(intent.AttemptId, utcNow()));
        return dispatched == JournalWrite.Written ? SubmissionOutcome.Dispatched : SubmissionOutcome.DispatchUnrecorded;
    }
}
