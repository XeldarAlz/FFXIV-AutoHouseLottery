using System.Text.Json.Serialization;

namespace AutoHouseLottery.Core.Persistence.Model;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(JournalRecordJson))]
internal sealed partial class JournalJsonContext : JsonSerializerContext
{
}
