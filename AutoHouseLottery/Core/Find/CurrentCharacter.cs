using AutoHouseLottery.Core.Eligibility.Model;
using AutoHouseLottery.Core.Housing.Model;
using ECommons.DalamudServices;

namespace AutoHouseLottery.Core.Find;

// A thin stand-in for the character context reader: the logged-in character's content ID, name and home world, read on
// the framework thread. Everything else stays unknown, so eligibility stays Unknown rather than eligible. The global
// Dalamud runs only on the global client, so the region is Global.
internal sealed class CurrentCharacter
{
    private CharacterProfile? cached;

    public CharacterProfile? Read()
    {
        if (!Svc.ClientState.IsLoggedIn || !Svc.PlayerState.IsLoaded)
        {
            cached = null;
            return null;
        }

        var contentId = Svc.PlayerState.ContentId;
        var homeWorld = Svc.PlayerState.HomeWorld.RowId;
        if (cached is { } known && known.ContentId == contentId && known.HomeWorldId == homeWorld)
        {
            return known;
        }

        cached = new CharacterProfile
        {
            ContentId = contentId,
            Name = Svc.PlayerState.CharacterName,
            Region = GameRegion.Global,
            HomeWorldId = homeWorld,
        };
        return cached;
    }
}
