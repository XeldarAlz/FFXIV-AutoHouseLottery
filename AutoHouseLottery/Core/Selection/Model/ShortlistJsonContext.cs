using System.Text.Json.Serialization;

namespace AutoHouseLottery.Core.Selection.Model;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true,
    UseStringEnumConverter = true,
    WriteIndented = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(ShortlistJson))]
internal sealed partial class ShortlistJsonContext : JsonSerializerContext
{
}
