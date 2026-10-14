using AutoHouseLottery.Core.Game.Model;
using ECommons.DalamudServices;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Info;

namespace AutoHouseLottery.Core.Game.Character;

internal static unsafe class CharacterContextReader
{
    private const ulong NoFreeCompany = 0;
    private const uint NoGrandCompany = 0;
    private const ushort NoTerritory = 0;
    private const ulong UnsetHouseId = ulong.MaxValue;
    private const sbyte NoIndex = -1;
    private const byte NoDivision = 0;

    public static CharacterContext? Read()
    {
        if (!Svc.ClientState.IsLoggedIn || !Svc.PlayerState.IsLoaded)
        {
            return null;
        }

        var playerState = Svc.PlayerState;
        var grandCompanyId = playerState.GrandCompany.RowId;
        var freeCompany = InfoProxyFreeCompany.Instance();
        var freeCompanyKnown = ReadFreeCompanyId() is not null;
        var companyTag = Svc.Objects.LocalPlayer?.CompanyTag.TextValue;
        var inventory = InventoryManager.Instance();
        return new CharacterContext(
            playerState.ContentId,
            playerState.CharacterName,
            playerState.HomeWorld.RowId,
            playerState.CurrentWorld.RowId,
            inventory == null ? null : inventory->GetGil(),
            playerState.Level,
            grandCompanyId,
            grandCompanyId == NoGrandCompany ? null : PlayerState.Instance()->GetGrandCompanyRank(),
            freeCompanyKnown ? freeCompany->Id : null,
            freeCompanyKnown ? freeCompany->NameString : null,
            string.IsNullOrEmpty(companyTag) ? null : companyTag,
            freeCompanyKnown ? freeCompany->HomeWorldId : null,
            ReadLocation(),
            OwnedEstate(EstateType.PersonalEstate),
            OwnedEstate(EstateType.FreeCompanyEstate),
            OwnedEstate(EstateType.ApartmentRoom),
            DateTime.UtcNow);
    }

    // Null when the character has no Free Company or its record is not loaded; the client does not tell the two apart.
    public static ulong? ReadFreeCompanyId()
    {
        var freeCompany = InfoProxyFreeCompany.Instance();
        return freeCompany != null && freeCompany->Id != NoFreeCompany ? freeCompany->Id : null;
    }

    // The ID of the character's own house, null when it owns none or the game has not loaded it.
    public static ulong? ReadPersonalEstateId() => OwnedEstate(EstateType.PersonalEstate)?.RawId;

    public static long? ReadGil()
    {
        var inventory = InventoryManager.Instance();
        return inventory == null ? null : inventory->GetGil();
    }

    public static HousingLocation ReadLocation()
    {
        var territoryTypeId = Svc.ClientState.TerritoryType;
        var manager = HousingManager.Instance();
        if (manager == null)
        {
            return HousingLocation.FromRaw(territoryTypeId, NoIndex, NoIndex, NoDivision, false);
        }

        return HousingLocation.FromRaw(territoryTypeId, manager->GetCurrentWard(), manager->GetCurrentPlot(), manager->GetCurrentDivision(), manager->IsInside());
    }

    // The house ID the game keeps for an owned estate; an unset ID or a territory of 0 means there is none or it is not loaded yet.
    private static EstateAddress? OwnedEstate(EstateType type)
    {
        var houseId = HousingManager.GetOwnedHouseId(type);
        if (houseId.Id == UnsetHouseId || houseId.TerritoryTypeId == NoTerritory)
        {
            return null;
        }

        return new EstateAddress(houseId.TerritoryTypeId, houseId.WorldId, houseId.WardIndex, houseId.PlotIndex, houseId.IsApartment, houseId.Id);
    }
}
