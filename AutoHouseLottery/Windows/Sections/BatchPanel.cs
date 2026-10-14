using System.Text;
using AutoHouseLottery.Core.Batch.Model;
using AutoHouseLottery.Core.Integration;
using AutoHouseLottery.Core.Localization;
using AutoHouseLottery.Core.Tasks;
using AutoHouseLottery.Core.Tasks.Model;
using AutoHouseLottery.Windows.Components;
using AutoHouseLottery.Windows.Find;
using Dalamud.Interface;
using System.Numerics;

namespace AutoHouseLottery.Windows.Sections;

// The batch above the run card: where it stands, every selected character's outcome with the entries first, and the
// action that fits. A batch that waits for a login or was halted goes on only from Continue; a finished one is dismissed.
internal static class BatchPanel
{
    private const long RecheckMs = Core.TimeUnits.MillisecondsPerSecond;

    private static StartRefusal continueRefusal = StartRefusal.NoAssignment;
    private static long continueCheckedMs;

    public static bool Visible(RunController controller) => controller.Batch is not null;

    public static void Draw(Plugin plugin)
    {
        var controller = plugin.Controller;
        if (controller.Batch is not { } batch)
        {
            return;
        }

        var waiting = batch.Phase is BatchPhase.WaitingForLogin or BatchPhase.Halted;
        var refusal = waiting ? ContinueRefusal(controller) : StartRefusal.None;
        var text = BatchPanelText.Shared.For(batch, refusal);
        var card = NoticeCard.Begin(text.Icon, text.Title, text.Body, text.Members, text.Accent, hasButtons: true);
        switch (batch.Phase)
        {
            case BatchPhase.Running or BatchPhase.Switching:
                if (!controller.Running && NoticeCard.Button(ref card, "##ahl_batch_stop", Loc.T(L.Common.StopRun), Styling.AccentRose, PillButton.Emphasis.Ghost, FontAwesomeIcon.Stop))
                {
                    controller.Stop();
                }

                break;
            case BatchPhase.WaitingForLogin or BatchPhase.Halted:
                if (NoticeCard.Button(ref card, "##ahl_batch_continue", Loc.T(L.Batch.Continue), Styling.AccentMint, PillButton.Emphasis.Filled, FontAwesomeIcon.Play, refusal == StartRefusal.None))
                {
                    continueRefusal = controller.ContinueBatch();
                    continueCheckedMs = Environment.TickCount64;
                }

                if (NoticeCard.Button(ref card, "##ahl_batch_discard", Loc.T(L.Batch.Discard), Styling.AccentNebula, PillButton.Emphasis.Ghost, FontAwesomeIcon.Times))
                {
                    controller.DismissBatch();
                }

                break;
            default:
                if (NoticeCard.Button(ref card, "##ahl_batch_dismiss", Loc.T(L.Run.Dismiss), Styling.AccentNebula, PillButton.Emphasis.Ghost, FontAwesomeIcon.Times))
                {
                    controller.DismissBatch();
                }

                break;
        }

        NoticeCard.End(card);
    }

    // The same checks Continue makes, asked once a second rather than every frame.
    private static StartRefusal ContinueRefusal(RunController controller)
    {
        var now = Environment.TickCount64;
        if (now - continueCheckedMs >= RecheckMs)
        {
            continueRefusal = controller.ContinueCheck();
            continueCheckedMs = now;
        }

        return continueRefusal;
    }
}

// The batch card's words, formatted once per checkpoint and language, so a frame allocates nothing.
internal sealed class BatchPanelText
{
    public static readonly BatchPanelText Shared = new();

    private BatchCheckpoint? builtFor;
    private StartRefusal builtRefusal;
    private LanguageInfo? builtLanguage;

    public FontAwesomeIcon Icon { get; private set; }

    public Vector4 Accent { get; private set; }

    public string Title { get; private set; } = string.Empty;

    public string Body { get; private set; } = string.Empty;

    public string Members { get; private set; } = string.Empty;

    // Refusal: why Continue would not go on now, said on the card itself; None while Continue would go on.
    public BatchPanelText For(BatchCheckpoint batch, StartRefusal refusal)
    {
        if (ReferenceEquals(batch, builtFor) && refusal == builtRefusal && ReferenceEquals(Loc.Current, builtLanguage))
        {
            return this;
        }

        builtFor = batch;
        builtRefusal = refusal;
        builtLanguage = Loc.Current;
        var report = BatchRules.Report(batch);
        var summary = Loc.T(L.Batch.Summary, report.Entered, report.DryRun, report.Queued + report.Running, report.NotEntered + report.Excluded);
        (Icon, Accent, Title, Body) = batch.Phase switch
        {
            BatchPhase.Running => (FontAwesomeIcon.Users, Styling.AccentBlue, Loc.T(L.Batch.TitleRunning), summary),
            BatchPhase.Switching => (FontAwesomeIcon.ExchangeAlt, Styling.AccentBlue, Loc.T(L.Batch.TitleSwitching, NameOf(batch, batch.SwitchTarget)), Loc.T(L.Batch.SwitchingBody)),
            BatchPhase.WaitingForLogin => (FontAwesomeIcon.HourglassHalf, Styling.AccentAmber, Loc.T(L.Batch.TitleWaiting), string.Concat(Loc.T(L.Batch.WaitingBody), " ", summary)),
            BatchPhase.Halted => (FontAwesomeIcon.PauseCircle, Styling.AccentAmber, Loc.T(L.Batch.TitleHalted), string.Concat(HaltText(batch.Halt), " ", summary)),
            _ => (FontAwesomeIcon.CheckCircle, report.Entered + report.DryRun > 0 ? Styling.AccentMint : Styling.AccentAmber, Loc.T(L.Batch.TitleFinished),
                batch.Halt == BatchHalt.None ? summary : string.Concat(HaltText(batch.Halt), " ", summary)),
        };
        if (refusal != StartRefusal.None && !(batch.Phase == BatchPhase.WaitingForLogin && refusal == StartRefusal.CharacterNotInPolicy))
        {
            Body = string.Concat(Body, " ", refusal == StartRefusal.CharacterNotInPolicy ? Loc.T(L.Batch.WaitingBody) : RunText.Refusal(refusal));
        }

        Members = MemberLines(batch);
        return this;
    }

    private static string MemberLines(BatchCheckpoint batch)
    {
        var order = new int[batch.Members.Length];
        for (var index = 0; index < order.Length; index++)
        {
            order[index] = index;
        }

        Array.Sort(order, (left, right) =>
        {
            var byState = BatchRules.DisplayRank(batch.Members[left].State).CompareTo(BatchRules.DisplayRank(batch.Members[right].State));
            return byState != 0 ? byState : left.CompareTo(right);
        });
        var lines = new StringBuilder();
        for (var index = 0; index < order.Length; index++)
        {
            if (lines.Length > 0)
            {
                lines.Append('\n');
            }

            lines.Append(Line(batch.Members[order[index]]));
        }

        return lines.ToString();
    }

    private static string Line(BatchMember member)
    {
        var name = member.Name.Length > 0 ? member.Name : Loc.T(L.Lottery.UnknownCharacter);
        var address = member.Plot is { } plot ? PlotNames.Address(plot) : Loc.T(L.Lottery.PlotNotNamed);
        var deposit = member.Deposit is { } amount ? Loc.Gil(amount) : Loc.T(L.Find.Unknown);
        return member.State switch
        {
            BatchMemberState.Entered => Loc.T(L.Batch.MemberEntered, name, address, deposit),
            BatchMemberState.DryRun => Loc.T(L.Batch.MemberDryRun, name, address, deposit),
            BatchMemberState.Running => Loc.T(L.Batch.MemberRunning, name),
            BatchMemberState.Uncertain => Loc.T(L.Batch.MemberUncertain, name),
            BatchMemberState.NotEntered when member.Failure != EntryFailure.None => Loc.T(L.Batch.MemberNotEntered, name, RunText.Failure(member.Failure, null)),
            BatchMemberState.NotEntered or BatchMemberState.Excluded => Loc.T(L.Batch.MemberExcluded, name, FindText.Reasons(member.Reasons)),
            _ => Loc.T(L.Batch.MemberQueued, name),
        };
    }

    private static string NameOf(BatchCheckpoint batch, ulong? contentId)
    {
        var index = contentId is { } id ? batch.IndexOf(id) : -1;
        return index >= 0 ? batch.Members[index].Name : Loc.T(L.Lottery.UnknownCharacter);
    }

    private static string Halt(BatchHalt halt) => Loc.T(halt switch
    {
        BatchHalt.StoppedByPlayer => L.Batch.HaltStopped,
        BatchHalt.CharacterChanged => L.Batch.HaltCharacterChanged,
        BatchHalt.WrongCharacter => L.Batch.HaltWrongCharacter,
        BatchHalt.SwitchFailed => L.Batch.HaltSwitchFailed,
        BatchHalt.NeedsReconciliation => L.Batch.HaltReconciliation,
        BatchHalt.JournalUnavailable => L.Batch.HaltJournal,
        BatchHalt.CheckpointNotSaved => L.Batch.HaltCheckpoint,
        BatchHalt.Reloaded => L.Batch.HaltReloaded,
        BatchHalt.CycleEnded => L.Batch.HaltCycleEnded,
        BatchHalt.SwitchBlocked => L.Batch.HaltSwitchBlocked,
        _ => L.Batch.HaltInterrupted,
    });

    private static string HaltText(BatchHalt halt) => halt == BatchHalt.AutoAnswerPlugin ? RunText.Refusal(StartRefusal.AutoAnswerPlugin) : Halt(halt);
}
