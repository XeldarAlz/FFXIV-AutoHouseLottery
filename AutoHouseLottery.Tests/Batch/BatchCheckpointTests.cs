using System.IO;
using System.Text;
using System.Threading.Tasks;
using AutoHouseLottery.Core.Batch.Model;
using AutoHouseLottery.Core.Eligibility.Model;
using AutoHouseLottery.Core.Housing.Model;
using AutoHouseLottery.Core.Persistence;
using AutoHouseLottery.Core.Persistence.Model;
using AutoHouseLottery.Core.Selection.Model;
using AutoHouseLottery.Core.Tasks.Model;
using static AutoHouseLottery.Tests.HousingFixtures;

namespace AutoHouseLottery.Tests.Batch;

// VALIDATION_PLAN section 6, "During character logout/login": the checkpoint written before a switch is all that survives
// it, and the batch resumes only for the character it matches.
public sealed class BatchCheckpointTests : IDisposable
{
    private static readonly DateTime now = CycleStart.AddHours(12);

    private readonly string directory = Path.Combine(Path.GetTempPath(), "ahl-batch-" + Guid.NewGuid().ToString("N"));
    private readonly List<string> reported = [];

    public BatchCheckpointTests()
    {
        Directory.CreateDirectory(directory);
    }

    public void Dispose() => Directory.Delete(directory, recursive: true);

    [Fact]
    public void ACheckpointReadsBackExactlyAsWritten()
    {
        var batch = BatchRulesTests.Batch(out _);
        batch = BatchRules.Record(batch, 0, BatchMemberState.Entered, EntryFailure.None, batch.Members[0].Assignment!.Value.TargetOrder[1], Deposit, now);
        batch = BatchRules.Skip(batch, 1, EligibilityReason.InsufficientGil | EligibilityReason.AlreadyEnteredThisCycle, EntryFailure.OverBudget, now);
        batch = BatchRules.BeginSwitch(batch, batch.Members[2].ContentId, now);

        Assert.True(BatchCodec.TryDeserialize(BatchCodec.Serialize(batch), out var read));

        Assert.Equal(batch.BatchId, read.BatchId);
        Assert.Equal(batch.Phase, read.Phase);
        Assert.Equal(batch.SwitchTarget, read.SwitchTarget);
        Assert.Equal(batch.DryRunRequested, read.DryRunRequested);
        Assert.Equal(batch.Policy.Cycle, read.Policy.Cycle);
        Assert.Equal<CharacterRunSettings>(batch.Policy.Characters, read.Policy.Characters);
        Assert.Equal<PlotKey>(batch.Policy.Shortlist!.Value.Plots, read.Policy.Shortlist!.Value.Plots);
        Assert.Equal(batch.Ranking.Length, read.Ranking.Length);
        for (var index = 0; index < batch.Members.Length; index++)
        {
            var written = batch.Members[index];
            var loaded = read.Members[index];
            Assert.Equal(written with { Assignment = null }, loaded with { Assignment = null });
            Assert.Equal<PlotKey>(written.Assignment!.Value.TargetOrder, loaded.Assignment!.Value.TargetOrder);
            Assert.Equal(written.Assignment.Value with { TargetOrder = default }, loaded.Assignment.Value with { TargetOrder = default });
        }
    }

    [Fact]
    public void AnExactTargetSurvivesTheCheckpoint()
    {
        var keys = BatchPlanningTests.Ward(3);
        var roster = new[] { CharacterFixtures.Character(1) };
        var policy = BatchPlanningTests.Policy(keys, BatchPlanningTests.Settings(1) with { Target = keys[2] });
        var batch = BatchRules.Create(Guid.NewGuid(), policy, AssignmentPlanner.Build(policy, roster, BatchPlanningTests.Listed(keys), now), roster, true, now);

        Assert.True(BatchCodec.TryDeserialize(BatchCodec.Serialize(batch), out var read));

        Assert.Equal(keys[2], read.Policy.Characters[0].Target);
        Assert.True(read.Members[0].Assignment!.Value.Exact);
    }

    [Fact]
    public void AnIncompleteOrForeignFileIsRefusedWhole()
    {
        var bytes = BatchCodec.Serialize(BatchRulesTests.Batch(out _));
        var text = Encoding.UTF8.GetString(bytes);

        Assert.False(BatchCodec.TryDeserialize(Encoding.UTF8.GetBytes(text.Replace("\"schema\": 1", "\"schema\": 2")), out _));
        Assert.False(BatchCodec.TryDeserialize(Encoding.UTF8.GetBytes(text.Replace("\"maximumDeposit\"", "\"somethingElse\"")), out _));
        Assert.False(BatchCodec.TryDeserialize("{ not json"u8, out _));
    }

    [Fact]
    public void AnInconsistentFileIsRefusedRatherThanActedOn()
    {
        var batch = BatchRulesTests.Batch(out _);
        var stranger = batch.Members[0] with { ContentId = 99, Settings = batch.Members[0].Settings with { ContentId = 99 } };
        var text = Encoding.UTF8.GetString(BatchCodec.Serialize(batch));

        Assert.False(BatchCodec.TryDeserialize(BatchCodec.Serialize(batch with { Members = batch.Members.SetItem(0, batch.Members[0] with { Assignment = null }) }), out _));
        Assert.False(BatchCodec.TryDeserialize(BatchCodec.Serialize(batch with { Members = batch.Members.SetItem(0, stranger) }), out _));
        Assert.False(BatchCodec.TryDeserialize(BatchCodec.Serialize(batch with { Members = batch.Members.Add(batch.Members[0]) }), out _));
        Assert.False(BatchCodec.TryDeserialize(BatchCodec.Serialize(batch with { Phase = BatchPhase.Switching }), out _));
        Assert.False(BatchCodec.TryDeserialize(BatchCodec.Serialize(batch with { SwitchTarget = 99 }), out _));
        Assert.False(BatchCodec.TryDeserialize(Encoding.UTF8.GetBytes(text.Replace("\"state\": \"Queued\"", "\"state\": 42")), out _));
        Assert.True(BatchCodec.TryDeserialize(BatchCodec.Serialize(batch), out _));
    }

    [Fact]
    public void ADamagedCheckpointIsKeptAsideAndTheBatchIsDropped()
    {
        var path = Path.Combine(directory, BatchCheckpointFile.FileName);
        File.WriteAllText(path, "{ \"schema\": 1 }");
        var file = new BatchCheckpointFile(path, (_, message) => reported.Add(message));

        Assert.Null(file.Load(now));

        Assert.False(File.Exists(path));
        Assert.Single(Directory.GetFiles(directory));
        Assert.Single(reported);
    }

    [Fact]
    public async Task ABatchSurvivesALogoutAndResumesOnlyForTheCharacterItMatches()
    {
        var path = Path.Combine(directory, BatchCheckpointFile.FileName);
        var file = new BatchCheckpointFile(path, (_, message) => reported.Add(message));
        var batch = BatchRulesTests.Batch(out _);
        var first = batch.Members[0];
        var plot = first.Assignment!.Value.TargetOrder[0];
        batch = BatchRules.Begin(batch, 0, now);
        batch = BatchRules.Record(batch, 0, BatchMemberState.DryRun, EntryFailure.None, plot, Deposit, now);
        var next = BatchRules.Next(batch, first.ContentId, switchingAllowed: true);
        Assert.Equal(BatchStepKind.Switch, next.Kind);
        batch = BatchRules.BeginSwitch(batch, batch.Members[next.MemberIndex].ContentId, now);
        Assert.True(await file.Save(batch));

        var reloaded = BatchRules.Restore(new BatchCheckpointFile(path, (_, message) => reported.Add(message)).Load(now)!, now);

        Assert.Equal(BatchPhase.Halted, reloaded.Phase);
        Assert.Equal(BatchHalt.Reloaded, reloaded.Halt);
        Assert.Equal(BatchMemberState.DryRun, reloaded.Members[0].State);
        Assert.Equal<PlotKey>([plot], reloaded.Members[1].Assignment!.Value.TargetOrder);
        Assert.Equal(LoginVerdict.NotInBatch, BatchRules.OnLogin(reloaded, first.ContentId, HomeWorld));
        var arrived = reloaded.Members[next.MemberIndex];
        Assert.Equal(LoginVerdict.QueuedCharacter, BatchRules.OnLogin(reloaded, arrived.ContentId, arrived.HomeWorldId));
        var resumed = BatchRules.Resume(reloaded, now);
        var step = BatchRules.Next(resumed, arrived.ContentId, switchingAllowed: false);
        Assert.Equal(BatchStepKind.Process, step.Kind);
        Assert.Equal(arrived.ContentId, resumed.Members[step.MemberIndex].ContentId);
        Assert.Empty(reported);
    }

    [Fact]
    public async Task SavingNothingRemovesTheCheckpoint()
    {
        var path = Path.Combine(directory, BatchCheckpointFile.FileName);
        var file = new BatchCheckpointFile(path, (_, message) => reported.Add(message));
        Assert.True(await file.Save(BatchRulesTests.Batch(out _)));

        Assert.True(await file.Save(null));

        Assert.False(File.Exists(path));
        Assert.Null(file.Load(now));
    }
}
