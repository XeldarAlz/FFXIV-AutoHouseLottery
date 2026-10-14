using AutoHouseLottery.Core.Game.Model;
using ECommons.DalamudServices;
using FFXIVClientStructs.FFXIV.Client.Game;

namespace AutoHouseLottery.Core.Game.Lottery;

// The character's own lottery entry. ClientStructs types HousingManager only up to +0x20, so the record pointer is read
// at the offset HouseHunter's overlay uses (no/w.cs: a pointer at HousingManager + 0xB0). The record is copied on every
// read; neither the pointer nor the record is kept.
internal static unsafe class LotteryReader
{
    private const int RecordPointerOffset = 0xB0;

    public static LotterySnapshot Read()
    {
        var readAtUtc = DateTime.UtcNow;
        if (!Svc.ClientState.IsLoggedIn || !Svc.PlayerState.IsLoaded)
        {
            return new LotterySnapshot(LotterySnapshotState.NoCharacter, 0, null, readAtUtc);
        }

        var contentId = Svc.PlayerState.ContentId;
        var manager = HousingManager.Instance();
        if (manager == null)
        {
            return new LotterySnapshot(LotterySnapshotState.ManagerMissing, contentId, null, readAtUtc);
        }

        var record = *(byte**)((byte*)manager + RecordPointerOffset);
        if (record == null)
        {
            return new LotterySnapshot(LotterySnapshotState.Unloaded, contentId, null, readAtUtc);
        }

        Span<byte> copy = stackalloc byte[LotteryEntryLayout.Length];
        new ReadOnlySpan<byte>(record, LotteryEntryLayout.Length).CopyTo(copy);
        var state = LotteryEntryLayout.Parse(copy, out var entry);
        return new LotterySnapshot(state, contentId, state == LotterySnapshotState.Active ? entry : null, readAtUtc);
    }

    public static string Describe(in LotterySnapshot snapshot)
    {
        if (snapshot.Entry is not { } entry)
        {
            return $"lottery {snapshot.State}";
        }

        return $"lottery {snapshot.State}: territory {entry.TerritoryTypeId}, ward {entry.WardIndex + 1}, plot {entry.PlotIndex + 1}, "
             + $"number {entry.LotteryNumber}, buyer {entry.BuyerType?.ToString() ?? $"unknown ({entry.RawBuyerType})"}, "
             + $"phase {entry.Phase?.ToString() ?? $"unknown ({entry.RawPhase})"}";
    }
}
