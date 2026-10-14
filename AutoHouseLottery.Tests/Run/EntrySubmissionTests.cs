using System.IO;
using System.Threading.Tasks;
using AutoHouseLottery.Core.Game.Model;
using AutoHouseLottery.Core.Housing.Model;
using AutoHouseLottery.Core.Persistence;
using AutoHouseLottery.Core.Persistence.Model;
using AutoHouseLottery.Core.Tasks.Model;
using static AutoHouseLottery.Tests.HousingFixtures;
using static AutoHouseLottery.Tests.Integration.IntegrationFixtures;

namespace AutoHouseLottery.Tests.Run;

public sealed class EntrySubmissionTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "ahl-submit-" + Guid.NewGuid().ToString("N"));
    private readonly string path;
    private readonly ActionJournal journal;

    public EntrySubmissionTests()
    {
        Directory.CreateDirectory(directory);
        path = Path.Combine(directory, "journal.jsonl");
        journal = ActionJournal.Open(path, ObservedAt).Journal!;
    }

    public void Dispose()
    {
        journal.Dispose();
        File.SetAttributes(path, FileAttributes.Normal);
        Directory.Delete(directory, recursive: true);
    }

    [Fact]
    public async Task IntentIsDurableBeforeThePress()
    {
        var intent = Attempt(Plot(12, 5), AttemptState.Prepared);
        AttemptState? onDiskAtPress = null;

        var outcome = await Submit(intent, () =>
        {
            using var reopened = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(reopened);
            onDiskAtPress = reader.ReadToEnd().Contains(intent.AttemptId.ToString()) ? journal.Snapshot().Attempts[0].State : null;
            return DialogActionResult.Pressed;
        });

        Assert.Equal(SubmissionOutcome.Dispatched, outcome);
        Assert.Equal(AttemptState.Prepared, onDiskAtPress);
        Assert.Equal(AttemptState.Dispatched, Assert.Single(journal.Snapshot().Attempts).State);
    }

    [Fact]
    public async Task JournalFaultAbortsBeforeThePress()
    {
        File.SetAttributes(path, FileAttributes.ReadOnly);
        var pressed = false;

        var outcome = await Submit(Attempt(Plot(12, 5), AttemptState.Prepared), () =>
        {
            pressed = true;
            return DialogActionResult.Pressed;
        });

        Assert.Equal(SubmissionOutcome.JournalFault, outcome);
        Assert.False(pressed);
    }

    [Fact]
    public async Task TakenSlotAbortsBeforeThePress()
    {
        Assert.Equal(JournalWrite.Written, journal.Prepare(Attempt(Plot(3, 1), AttemptState.Prepared)));
        var pressed = false;

        var outcome = await Submit(Attempt(Plot(12, 5), AttemptState.Prepared), () =>
        {
            pressed = true;
            return DialogActionResult.Pressed;
        });

        Assert.Equal(SubmissionOutcome.SlotTaken, outcome);
        Assert.False(pressed);
    }

    [Fact]
    public async Task RefusedPressReleasesTheSlotAsRejected()
    {
        var intent = Attempt(Plot(12, 5), AttemptState.Prepared);

        var outcome = await Submit(intent, () => DialogActionResult.LabelMismatch);

        Assert.Equal(SubmissionOutcome.PressRefused, outcome);
        Assert.Equal(AttemptState.Rejected, Assert.Single(journal.Snapshot().Attempts).State);
        Assert.Null(journal.FindSlotHolder(intent.Key));
    }

    private Task<SubmissionOutcome> Submit(ActionAttempt intent, Func<DialogActionResult> press)
        => new EntrySubmission(journal).Submit(intent, () => Task.CompletedTask, press, () => ObservedAt);
}
