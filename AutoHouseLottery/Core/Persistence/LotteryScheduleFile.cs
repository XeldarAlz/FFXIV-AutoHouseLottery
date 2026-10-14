using System.Collections.Immutable;
using System.IO;
using System.Text.Json;
using AutoHouseLottery.Core.Housing.Model;
using AutoHouseLottery.Core.Persistence.Model;

namespace AutoHouseLottery.Core.Persistence;

internal static class LotteryScheduleFile
{
    public const int Schema = 1;

    public static bool TryLoad(string path, out ImmutableArray<LotterySchedule> schedules)
    {
        schedules = [];
        if (!File.Exists(path))
        {
            return true;
        }

        try
        {
            var document = JsonSerializer.Deserialize(File.ReadAllBytes(path), ScheduleJsonContext.Default.ScheduleFileJson);
            if (document.Schema != Schema || document.Schedules is not { } stored)
            {
                return false;
            }

            var loaded = new LotterySchedule[stored.Length];
            for (var index = 0; index < stored.Length; index++)
            {
                if (!stored[index].TryToSchedule(out loaded[index]))
                {
                    return false;
                }
            }

            schedules = [.. loaded];
            return true;
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    public static bool TrySave(string path, ReadOnlySpan<LotterySchedule> schedules)
    {
        var stored = new ScheduleJson[schedules.Length];
        for (var index = 0; index < schedules.Length; index++)
        {
            stored[index] = ScheduleJson.From(schedules[index]);
        }

        var bytes = JsonSerializer.SerializeToUtf8Bytes(new ScheduleFileJson { Schema = Schema, Schedules = stored }, ScheduleJsonContext.Default.ScheduleFileJson);
        try
        {
            JournalFile.RewriteAtomically(path, bytes);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
