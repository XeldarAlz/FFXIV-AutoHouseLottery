using System.Diagnostics.CodeAnalysis;
using AutoHouseLottery.Core.Housing.Model;

namespace AutoHouseLottery.Core.Persistence.Model;

internal static class JournalCodec
{
    public static JournalRecordJson Header(int schema) => new() { Kind = JournalRecordKind.Header, Schema = schema };

    public static JournalRecordJson Encode(ActionAttempt attempt) => new()
    {
        Kind = JournalRecordKind.Attempt,
        Attempt = new AttemptJson
        {
            Id = attempt.AttemptId,
            Kind = attempt.Kind,
            Run = attempt.RunId,
            Character = attempt.CharacterContentId,
            HomeWorld = attempt.HomeWorldId,
            Cycle = CycleKeyJson.From(attempt.Cycle),
            Plot = PlotKeyJson.From(attempt.Plot),
            Buyer = attempt.Buyer,
            Amount = attempt.Amount,
            PreparedUtc = attempt.PreparedUtc,
            State = attempt.State,
            UpdatedUtc = attempt.UpdatedUtc,
            Receipt = attempt.ReceiptId,
            GilBefore = attempt.GilBefore,
        },
    };

    public static JournalRecordJson Encode(EntryReceipt receipt) => new()
    {
        Kind = JournalRecordKind.Receipt,
        Receipt = new ReceiptJson
        {
            Id = receipt.ReceiptId,
            Character = receipt.CharacterContentId,
            HomeWorld = receipt.HomeWorldId,
            Cycle = CycleKeyJson.From(receipt.Cycle),
            Plot = PlotKeyJson.From(receipt.Plot),
            Buyer = receipt.Buyer,
            Evidence = receipt.Evidence,
            Attempt = receipt.AttemptId,
            EntryNumber = receipt.EntryNumber,
            Deposit = receipt.Deposit,
            EnteredUtc = receipt.EnteredUtc,
        },
    };

    public static JournalRecordJson Encode(in EntrySettlement settlement) => new()
    {
        Kind = JournalRecordKind.Settlement,
        Settlement = new SettlementJson
        {
            Receipt = settlement.ReceiptId,
            State = settlement.State,
            Refund = settlement.Refund,
            ExpectedRefund = settlement.ExpectedRefund,
            ReceivedRefund = settlement.ReceivedRefund,
            UpdatedUtc = settlement.UpdatedUtc,
        },
    };

    public static bool TryDecode(in AttemptJson json, [NotNullWhen(true)] out ActionAttempt? attempt)
    {
        attempt = null;
        if (json.Id is not { } id
            || json.Kind is not { } kind
            || json.Run is not { } run
            || json.Character is not { } character
            || json.HomeWorld is not { } homeWorld
            || json.Cycle is not { } cycleJson
            || !cycleJson.TryToKey(out var cycle)
            || json.Plot is not { } plotJson
            || !plotJson.TryToKey(out var plot)
            || json.Buyer is not { } buyer
            || json.Amount is not { } amount
            || json.PreparedUtc is not { } prepared
            || json.State is not { } state
            || json.UpdatedUtc is not { } updated)
        {
            return false;
        }

        if (kind != ActionKind.Entry && json.Receipt is null)
        {
            return false;
        }

        attempt = new ActionAttempt
        {
            AttemptId = id,
            Kind = kind,
            RunId = run,
            CharacterContentId = character,
            HomeWorldId = homeWorld,
            Cycle = cycle,
            Plot = plot,
            Buyer = buyer,
            Amount = amount,
            PreparedUtc = prepared,
            State = state,
            UpdatedUtc = updated,
            ReceiptId = json.Receipt,
            GilBefore = json.GilBefore,
        };
        return true;
    }

    public static bool TryDecode(in ReceiptJson json, [NotNullWhen(true)] out EntryReceipt? receipt)
    {
        receipt = null;
        if (json.Id is not { } id
            || json.Character is not { } character
            || json.HomeWorld is not { } homeWorld
            || json.Cycle is not { } cycleJson
            || !cycleJson.TryToKey(out var cycle)
            || json.Plot is not { } plotJson
            || !plotJson.TryToKey(out var plot)
            || json.Buyer is not { } buyer
            || json.Evidence is not { } evidence)
        {
            return false;
        }

        receipt = new EntryReceipt
        {
            ReceiptId = id,
            CharacterContentId = character,
            HomeWorldId = homeWorld,
            Cycle = cycle,
            Plot = plot,
            Buyer = buyer,
            Evidence = evidence,
            AttemptId = json.Attempt,
            EntryNumber = json.EntryNumber,
            Deposit = json.Deposit,
            EnteredUtc = json.EnteredUtc,
        };
        return true;
    }

    public static bool TryDecode(in SettlementJson json, out EntrySettlement settlement)
    {
        settlement = default;
        if (json.Receipt is not { } receipt
            || json.State is not { } state
            || json.Refund is not { } refund
            || json.UpdatedUtc is not { } updated)
        {
            return false;
        }

        settlement = new EntrySettlement(receipt, state, refund, json.ExpectedRefund, json.ReceivedRefund, updated);
        return true;
    }
}
