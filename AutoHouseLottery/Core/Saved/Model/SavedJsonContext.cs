using System.Text.Json.Serialization;

namespace AutoHouseLottery.Core.Saved.Model;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true,
    UseStringEnumConverter = true,
    WriteIndented = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(SavedFileJson))]
internal sealed partial class SavedJsonContext : JsonSerializerContext
{
}
