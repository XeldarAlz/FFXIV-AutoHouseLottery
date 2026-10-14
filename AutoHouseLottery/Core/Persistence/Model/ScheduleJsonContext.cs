using System.Text.Json.Serialization;

namespace AutoHouseLottery.Core.Persistence.Model;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true,
    WriteIndented = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(ScheduleFileJson))]
internal sealed partial class ScheduleJsonContext : JsonSerializerContext
{
}
