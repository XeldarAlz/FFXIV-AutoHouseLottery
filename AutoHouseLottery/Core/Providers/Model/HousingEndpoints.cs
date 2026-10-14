using System.Globalization;

namespace AutoHouseLottery.Core.Providers.Model;

internal static class HousingEndpoints
{
    public const string BaseUrl = "https://housing-api.yozoracho.dev/api/v1";

    public static string Worlds() => string.Concat(BaseUrl, "/worlds");

    public static string World(uint worldId) => string.Concat(BaseUrl, "/worlds/", worldId.ToString(CultureInfo.InvariantCulture));
}
