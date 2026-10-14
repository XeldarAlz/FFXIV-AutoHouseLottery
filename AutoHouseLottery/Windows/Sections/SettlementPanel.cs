using AutoHouseLottery.Core.Integration;
using AutoHouseLottery.Core.Localization;
using AutoHouseLottery.Core.Settlement;
using AutoHouseLottery.Core.Settlement.Model;
using AutoHouseLottery.Core.Tasks;
using AutoHouseLottery.Windows.Components;
using Dalamud.Interface;
using System.Numerics;

namespace AutoHouseLottery.Windows.Sections;

// The settlement visit going on or just ended: what it does, where, its step, and Stop while it may still be stopped;
// once ended, one card with what it found and Dismiss.
internal static class SettlementPanel
{
    public static bool Visible(RunController controller) => controller.Settling || controller.Settlement.VisitId != Guid.Empty;

    public static void Draw(Plugin plugin)
    {
        var controller = plugin.Controller;
        var text = SettlementPanelText.Shared.For(controller.Settlement);
        var status = controller.Settlement;
        if (controller.Settling)
        {
            var verifying = status.FinishingVerification;
            var active = NoticeCard.Begin(text.Icon, text.Title, text.Body, text.Facts, verifying ? Styling.AccentMint : Styling.AccentBlue, hasButtons: true);
            if (NoticeCard.Button(ref active, "##ahl_settlement_stop", Loc.T(L.Common.StopRun), Styling.AccentRose, PillButton.Emphasis.Ghost, FontAwesomeIcon.Stop, !verifying))
            {
                controller.StopSettlement();
            }

            NoticeCard.End(active);
            return;
        }

        var card = NoticeCard.Begin(text.Icon, text.Title, text.Body, text.Facts, text.Accent, hasButtons: true);
        if (NoticeCard.Button(ref card, "##ahl_settlement_dismiss", Loc.T(L.Run.Dismiss), Styling.AccentNebula, PillButton.Emphasis.Ghost, FontAwesomeIcon.Times))
        {
            controller.DismissSettlement();
        }

        NoticeCard.End(card);
    }
}

// The settlement card's words, formatted once per published visit status and language, so a frame allocates nothing.
internal sealed class SettlementPanelText
{
    public static readonly SettlementPanelText Shared = new();

    private const string Separator = " · ";

    private SettlementStatus? builtFor;
    private LanguageInfo? builtLanguage;

    public FontAwesomeIcon Icon { get; private set; }

    public Vector4 Accent { get; private set; }

    public string Title { get; private set; } = string.Empty;

    public string Body { get; private set; } = string.Empty;

    public string? Facts { get; private set; }

    public string StepLine { get; private set; } = string.Empty;

    public SettlementPanelText For(SettlementStatus status)
    {
        if (ReferenceEquals(status, builtFor) && ReferenceEquals(Loc.Current, builtLanguage))
        {
            return this;
        }

        builtFor = status;
        builtLanguage = Loc.Current;
        var address = Address(status);
        Facts = status.CharacterName.Length == 0 ? address : string.Concat(status.CharacterName, Separator, address);
        StepLine = SettlementText.Step(status.Step);
        if (status.Active)
        {
            Icon = status.Mode switch
            {
                SettlementMode.Claim => FontAwesomeIcon.Home,
                SettlementMode.Refund => FontAwesomeIcon.Coins,
                _ => FontAwesomeIcon.Search,
            };
            Accent = Styling.AccentBlue;
            Title = SettlementText.Title(status.Mode);
            Body = status.Real || status.Mode == SettlementMode.Inspect ? StepLine : string.Concat(StepLine, Separator, Loc.T(L.Settlement.DryRunNote));
            return this;
        }

        (Icon, Accent, Title, Body) = Outcome(status, address);
        return this;
    }

    private static (FontAwesomeIcon Icon, Vector4 Accent, string Title, string Body) Outcome(SettlementStatus status, string address)
    {
        switch (status.Result)
        {
            case SettlementResult.Refunded:
                return (FontAwesomeIcon.CheckCircle, Styling.AccentMint, Loc.T(L.Settlement.RefundedTitle), Loc.T(L.Settlement.RefundedBody, Loc.Gil(status.Received ?? 0), address));
            case SettlementResult.RefundedAmountDiffers:
                return (FontAwesomeIcon.ExclamationTriangle, Styling.AccentAmber, Loc.T(L.Settlement.RefundedTitle),
                    Loc.T(L.Settlement.RefundedDiffersBody, Loc.Gil(status.Received ?? 0), address, Loc.Gil(status.OfferedRefund ?? 0)));
            case SettlementResult.Claimed:
                return (FontAwesomeIcon.Home, Styling.AccentMint, Loc.T(L.Settlement.ClaimedTitle), Loc.T(L.Settlement.ClaimedBody, address));
            case SettlementResult.DryRun:
                return (FontAwesomeIcon.Flask, Styling.AccentBlue, Loc.T(L.Settlement.DryRunTitle), Loc.T(L.Settlement.DryRunBody, SettlementText.Outcome(status, address)));
            case SettlementResult.Classified:
                return (FontAwesomeIcon.Search, Styling.AccentBlue, Loc.T(L.Settlement.CheckedTitle), SettlementText.Outcome(status, address));
            case SettlementResult.NeedsReconciliation:
                return (FontAwesomeIcon.QuestionCircle, Styling.AccentRose, Loc.T(L.Settlement.UncertainTitle), Loc.T(L.Settlement.UncertainBody));
            case SettlementResult.Stopped:
                return (FontAwesomeIcon.StopCircle, Styling.AccentAmber, Loc.T(L.Settlement.StoppedTitle), SettlementText.Failure(status.Failure));
            default:
                return (FontAwesomeIcon.HandPaper, Styling.AccentAmber, Loc.T(L.Settlement.NeedsPlayerTitle), SettlementText.Failure(status.Failure));
        }
    }

    private static string Address(SettlementStatus status)
        => status.Plot is { } plot ? PlotNames.Address(plot) : status.AddressText ?? Loc.T(L.Lottery.PlotNotNamed);
}
