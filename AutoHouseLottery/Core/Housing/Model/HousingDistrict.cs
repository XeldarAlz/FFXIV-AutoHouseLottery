namespace AutoHouseLottery.Core.Housing.Model;

internal readonly record struct HousingDistrict(ushort TerritoryId, byte WardCount, byte PlotsPerWard);
