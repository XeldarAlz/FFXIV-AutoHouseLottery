using System.Collections.Immutable;
using AutoHouseLottery.Core.Eligibility.Model;
using AutoHouseLottery.Core.Game.Character;
using AutoHouseLottery.Core.Housing.Model;
using AutoHouseLottery.Core.Persistence.Model;
using ECommons.DalamudServices;

namespace AutoHouseLottery.Core.Integration;

// The logged-in character's profile, from the character context reader, the journal and its latest lottery reading,
// read on the framework thread. Read runs every frame and hands back the cached profile; once a second it compares what
// the profile is made of without allocating (the character, its gil, Free Company, own house and account group, the
// journal, the reading and the cycle) and rebuilds the profile only when one of them changed. A different character is
// read at once.
internal sealed class CurrentCharacter(HousingContext housing)
{
    private const long CheckIntervalMs = TimeUnits.MillisecondsPerSecond;

    private CharacterProfile? cached;
    private Inputs inputs;
    private long nextCheckMs;

    public CharacterProfile? Read()
    {
        if (!Svc.ClientState.IsLoggedIn || !Svc.PlayerState.IsLoaded)
        {
            cached = null;
            inputs = default;
            return null;
        }

        var now = Environment.TickCount64;
        var contentId = Svc.PlayerState.ContentId;
        if (cached is { } known && known.ContentId == contentId && now < nextCheckMs)
        {
            return known;
        }

        nextCheckMs = now + CheckIntervalMs;
        var position = housing.CurrentPosition(DateTime.UtcNow);
        var journal = housing.JournalSnapshot;
        var reading = housing.LastReading(contentId);
        var current = new Inputs(
            contentId,
            Svc.PlayerState.HomeWorld.RowId,
            CharacterContextReader.ReadGil(),
            CharacterContextReader.ReadFreeCompanyId(),
            CharacterContextReader.ReadPersonalEstateId(),
            housing.AccountGroup(contentId),
            journal.Attempts,
            journal.Receipts,
            journal.Settlements,
            reading?.ReadAtUtc,
            position.Cycle,
            position.Phase);
        if (cached is not null && current == inputs)
        {
            return cached;
        }

        inputs = current;
        cached = CharacterContextReader.Read() is { } context ? housing.Profile(context, reading, position) : null;
        return cached;
    }

    private readonly record struct Inputs(
        ulong ContentId,
        uint HomeWorldId,
        long? Gil,
        ulong? FreeCompanyId,
        ulong? PersonalEstateId,
        int? AccountGroup,
        ImmutableArray<ActionAttempt> Attempts,
        ImmutableArray<EntryReceipt> Receipts,
        ImmutableArray<EntrySettlement> Settlements,
        DateTime? ReadingAtUtc,
        CycleKey Cycle,
        LotteryPhase Phase);
}
