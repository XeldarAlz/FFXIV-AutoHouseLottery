using System.Text.Json.Nodes;

namespace AutoHouseLottery.Core.Persistence.Model;

internal readonly record struct JournalMigration(int FromSchema, Func<JsonObject, JsonObject> Upgrade);
