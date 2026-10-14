using AutoHouseLottery.Core.Game.Dialogs;
using AutoHouseLottery.Core.Game.Model;
using Dalamud;
using ECommons.DalamudServices;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.System.Framework;
using FFXIVClientStructs.FFXIV.Component.GUI;
using System.Runtime.InteropServices;

namespace AutoHouseLottery.Core.Game.Lottery;

// The character's own lottery entry. ClientStructs types HousingManager only up to +0x20 and offers no signature for the
// lottery record, so the record behind HousingManager + 0xB0 (HouseHunter's overlay, no/w.cs) is read only on a game
// build listed in LotteryRecordAccess.ValidatedGameVersions, through checked reads that cannot fault. Every other build
// reads the lottery lines the Timers window shows instead.
internal static unsafe class LotteryReader
{
    public const string TimersAddon = "ContentsInfo";
    // The Timers window's detail pane, if the lottery lines live there rather than in the list; unverified.
    public const string TimersDetailAddon = "ContentsInfoDetail";

    private const int RecordPointerOffset = 0xB0;
    private const int MaxWindowTexts = 128;
    private const int OneBased = 1;

    public static string GameVersion() => Framework.Instance()->GameVersionString;

    public static LotterySnapshot Read()
    {
        if (!CharacterLoaded(out var contentId, out var readAtUtc))
        {
            return new LotterySnapshot(LotterySnapshotState.NoCharacter, LotterySource.TimersText, 0, null, readAtUtc);
        }

        return LotteryRecordAccess.IsValidated(GameVersion()) ? ReadRecord(contentId, readAtUtc) : ReadTimersText(contentId, readAtUtc);
    }

    // For validating a build in game: the record read through the same checked reads, whatever the build. It cannot
    // fault; it is never used to decide anything.
    public static LotterySnapshot ReadRecordForValidation()
    {
        return CharacterLoaded(out var contentId, out var readAtUtc)
            ? ReadRecord(contentId, readAtUtc)
            : new LotterySnapshot(LotterySnapshotState.NoCharacter, LotterySource.Record, 0, null, readAtUtc);
    }

    public static string Describe(in LotterySnapshot snapshot)
    {
        if (snapshot.Entry is not { } entry)
        {
            return $"lottery {snapshot.State} from {snapshot.Source}";
        }

        return $"lottery {snapshot.State} from {snapshot.Source}: territory {Show(entry.TerritoryTypeId)}, ward {ShowOneBased(entry.WardIndex)}, plot {ShowOneBased(entry.PlotIndex)}, "
             + $"number {Show(entry.LotteryNumber)}, buyer {entry.BuyerType?.ToString() ?? $"unknown ({Show(entry.RawBuyerType)})"}, "
             + $"phase {entry.Phase?.ToString() ?? $"unknown ({Show(entry.RawPhase)})"}, address text '{entry.AddressText ?? "none"}', entry type text '{entry.EntryTypeText ?? "none"}'";
    }

    private static bool CharacterLoaded(out ulong contentId, out DateTime readAtUtc)
    {
        readAtUtc = DateTime.UtcNow;
        contentId = Svc.ClientState.IsLoggedIn && Svc.PlayerState.IsLoaded ? Svc.PlayerState.ContentId : 0;
        return contentId != 0;
    }

    private static LotterySnapshot ReadRecord(ulong contentId, DateTime readAtUtc)
    {
        var manager = HousingManager.Instance();
        if (manager == null)
        {
            return new LotterySnapshot(LotterySnapshotState.ManagerMissing, LotterySource.Record, contentId, null, readAtUtc);
        }

        if (!SafeMemory.Read<ulong>((nint)((byte*)manager + RecordPointerOffset), out var recordAddress) || recordAddress == 0)
        {
            return new LotterySnapshot(LotterySnapshotState.Unloaded, LotterySource.Record, contentId, null, readAtUtc);
        }

        if (!LotteryRecordAccess.IsPlausiblePointer(recordAddress)
            || !SafeMemory.ReadBytes((nint)recordAddress, LotteryEntryLayout.Length, out var record)
            || record.Length != LotteryEntryLayout.Length)
        {
            return new LotterySnapshot(LotterySnapshotState.Invalid, LotterySource.Record, contentId, null, readAtUtc);
        }

        var state = LotteryEntryLayout.Parse(record, out var entry);
        return new LotterySnapshot(state, LotterySource.Record, contentId, state == LotterySnapshotState.Active ? entry : null, readAtUtc);
    }

    private static LotterySnapshot ReadTimersText(ulong contentId, DateTime readAtUtc)
    {
        var texts = new List<string>();
        CollectWindowTexts(TimersAddon, texts);
        CollectWindowTexts(TimersDetailAddon, texts);
        var state = LotteryWindowText.Parse(CollectionsMarshal.AsSpan(texts), HousingSheetText.LotteryPatterns(), out var entry);
        return new LotterySnapshot(state, LotterySource.TimersText, contentId, state == LotterySnapshotState.Active ? entry : null, readAtUtc);
    }

    // The string values and the window's own text nodes, since where the lottery lines sit is not documented.
    public static void CollectWindowTexts(string addonName, List<string> destination)
    {
        if (!AddonReads.TryGetReady(addonName, out var addon))
        {
            return;
        }

        if (addon->AtkValues != null)
        {
            for (var valueIndex = 0; valueIndex < addon->AtkValuesCount && destination.Count < MaxWindowTexts; valueIndex++)
            {
                ref readonly var value = ref addon->AtkValues[valueIndex];
                if (AddonReads.IsText(value) && AddonReads.Text(value) is { Length: > 0 } text)
                {
                    destination.Add(text);
                }
            }
        }

        var nodes = addon->UldManager.NodeList;
        for (var nodeIndex = 0; nodeIndex < addon->UldManager.NodeListCount && destination.Count < MaxWindowTexts; nodeIndex++)
        {
            var node = nodes[nodeIndex];
            if (node != null && node->Type == NodeType.Text && AddonReads.NodeText((AtkTextNode*)node) is { Length: > 0 } text)
            {
                destination.Add(text);
            }
        }
    }

    private static string Show<T>(T? value) where T : struct => value?.ToString() ?? "unknown";

    private static string ShowOneBased(int? index) => index is { } value ? (value + OneBased).ToString() : "unknown";
}
