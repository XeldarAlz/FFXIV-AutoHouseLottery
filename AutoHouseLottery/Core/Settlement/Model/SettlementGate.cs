using AutoHouseLottery.Core.Housing.Model;
using AutoHouseLottery.Core.Persistence.Model;

namespace AutoHouseLottery.Core.Settlement.Model;

internal static class SettlementCapabilities
{
    // True only once a supervised real refund has confirmed the refund contract (Yes on Addon rows 7124 and 7123, the gil
    // returned and the entry cleared) on the supported build. Until then a refund is only ever inspected and closed.
    public const bool RefundConfirmValidated = false;

    // True only once a supervised real claim has confirmed the claim contract (Yes on Addon rows 7122 and 7128 and the
    // estate showing in the character's estate list) on the supported build.
    public const bool ClaimConfirmValidated = false;

    // True only once supervised visits have shown that a placard without a result reliably means the entry was settled
    // outside the plugin (the placard's buttons, the record reading and the two-session rule). Until then such an entry
    // stays pending and is never recorded as refunded, expired or not entered.
    public const bool AbsenceSettlementValidated = false;
}

// A maintainer's permission for one real refund or one real claim: one kind, one character, one receipt on one plot. It
// lives in memory only, so it ends at logout, at unload, and with the one press it was given for.
internal sealed record SettlementGrant(ActionKind Kind, ulong ContentId, string CharacterName, PlotKey Plot, Guid ReceiptId, DateTime IssuedUtc)
{
    public bool Used { get; init; }

    public bool Covers(ActionKind kind, ulong contentId, PlotKey plot, Guid receiptId)
        => !Used && kind == Kind && contentId == ContentId && plot == Plot && receiptId == ReceiptId;
}

internal static class SettlementGate
{
    // Asked with the live dialog's plot right before Yes. A requested dry run never presses. While the capability is not
    // validated, only a grant naming exactly this kind of action, this character, this receipt and this plot opens Yes.
    public static bool MayPress(ActionKind kind, bool validated, bool dryRun, SettlementGrant? grant, ulong contentId, PlotKey plot, Guid receiptId)
    {
        if (kind == ActionKind.Entry || dryRun)
        {
            return false;
        }

        if (validated)
        {
            return true;
        }

        return grant is not null && grant.Covers(kind, contentId, plot, receiptId);
    }

    public static bool Validated(ActionKind kind) => kind switch
    {
        ActionKind.Refund => SettlementCapabilities.RefundConfirmValidated,
        ActionKind.Claim => SettlementCapabilities.ClaimConfirmValidated,
        _ => false,
    };

    // A player's click asks for a dry run until the action it can lead to has been validated in game, and a requested dry
    // run always wins, so a grant never turns a click real. Before that, a real refund or claim comes only from a grant for
    // that receipt, used by the dev command with the word real or, for a refund, by the scheduler.
    public static bool PlayerDryRun(SettlementMode mode) => !Validated(ActionOf(mode));

    public static ActionKind ActionOf(SettlementMode mode) => mode switch
    {
        SettlementMode.Refund => ActionKind.Refund,
        SettlementMode.Claim => ActionKind.Claim,
        _ => ActionKind.Entry,
    };
}
