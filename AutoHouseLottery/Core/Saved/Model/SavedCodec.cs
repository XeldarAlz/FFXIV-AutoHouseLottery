using System.Collections.Immutable;
using System.Text.Json;
using AutoHouseLottery.Core.Find.Model;
using AutoHouseLottery.Core.Housing.Model;

namespace AutoHouseLottery.Core.Saved.Model;

// One damaged search or list is skipped on its own, so a hand edit never costs the player the rest of their saved work.
internal static class SavedCodec
{
    public const int Schema = 1;
    public const int MaximumRecent = 40;

    public static string Export(in SavedLibrarySnapshot library, IReadOnlyDictionary<string, DateTime> delivered, IReadOnlyList<NoticeRecord> recent, NoticeBaseline baseline)
    {
        var searches = new SavedSearchJson[library.Searches.Length];
        for (var index = 0; index < searches.Length; index++)
        {
            searches[index] = ToJson(library.Searches[index]);
        }

        var lists = new SavedListJson[library.Lists.Length];
        for (var index = 0; index < lists.Length; index++)
        {
            lists[index] = ToJson(library.Lists[index]);
        }

        var ledger = new DeliveredNoticeJson[delivered.Count];
        var ledgerIndex = 0;
        foreach (var entry in delivered)
        {
            ledger[ledgerIndex++] = new DeliveredNoticeJson { Key = entry.Key, AtUtc = entry.Value };
        }

        var records = new NoticeRecordJson[recent.Count];
        for (var index = 0; index < records.Length; index++)
        {
            var record = recent[index];
            records[index] = new NoticeRecordJson
            {
                Kind = record.Kind,
                Plot = PlotKeyJson.From(record.Plot),
                Source = record.SourceName,
                AtUtc = record.AtUtc,
                PhaseEndUtc = record.PhaseEndUtc,
            };
        }

        var pendingPlots = new PlotKeyJson[baseline.Plots.Count];
        var pendingIndex = 0;
        foreach (var plot in baseline.Plots)
        {
            pendingPlots[pendingIndex++] = PlotKeyJson.From(plot);
        }

        var file = new SavedFileJson
        {
            Schema = Schema,
            Searches = searches,
            Lists = lists,
            Delivered = ledger,
            Recent = records,
            PendingSources = [.. baseline.Sources],
            PendingPlots = pendingPlots,
        };
        return JsonSerializer.Serialize(file, SavedJsonContext.Default.SavedFileJson);
    }

    public static SavedReadError TryRead(string json, out SavedDocument? document)
    {
        document = null;
        SavedFileJson? file;
        try
        {
            file = JsonSerializer.Deserialize(json, SavedJsonContext.Default.SavedFileJson);
        }
        catch (JsonException)
        {
            return SavedReadError.Malformed;
        }

        if (file is null)
        {
            return SavedReadError.Malformed;
        }

        if (file.Schema != Schema)
        {
            return SavedReadError.UnsupportedSchema;
        }

        var skipped = 0;
        var searches = ImmutableArray.CreateBuilder<SavedSearch>();
        var savedSearches = file.Searches ?? [];
        for (var index = 0; index < savedSearches.Length; index++)
        {
            if (TryFromJson(savedSearches[index], out var search))
            {
                searches.Add(search);
                continue;
            }

            skipped++;
        }

        var lists = ImmutableArray.CreateBuilder<SavedList>();
        var savedLists = file.Lists ?? [];
        for (var index = 0; index < savedLists.Length; index++)
        {
            if (TryFromJson(savedLists[index], out var list))
            {
                lists.Add(list);
                continue;
            }

            skipped++;
        }

        document = new SavedDocument(
            searches.ToImmutable(),
            lists.ToImmutable(),
            ReadDelivered(file.Delivered),
            ReadRecent(file.Recent),
            [.. file.PendingSources ?? []],
            ReadPlots(file.PendingPlots),
            skipped);
        return SavedReadError.None;
    }

    private static SavedSearchJson ToJson(SavedSearch search)
    {
        var area = search.Area;
        var filter = search.Filter;
        var districts = area.DistrictIds();
        var districtIds = new uint[districts.Length];
        for (var index = 0; index < districtIds.Length; index++)
        {
            districtIds[index] = districts[index];
        }

        return new SavedSearchJson
        {
            Id = search.Id,
            Name = search.Name,
            CreatedUtc = search.CreatedUtc,
            World = area.WorldId,
            Districts = districtIds,
            AllWards = area.AllWards,
            Wards = area.AllWards ? null : area.WardNumbers(),
            MainDivision = area.MainDivision,
            Subdivision = area.Subdivision,
            Sizes = filter.Sizes,
            Buyer = filter.Buyer,
            PriceCeiling = filter.PriceCeiling,
            Statuses = filter.Statuses,
            FirstPlot = filter.FirstPlot,
            LastPlot = filter.LastPlot,
            MinimumEntries = filter.MinimumEntries,
            MaximumEntries = filter.MaximumEntries,
            MaximumAgeHours = filter.MaximumAgeHours,
        };
    }

    private static SavedListJson ToJson(SavedList list)
    {
        var plots = new PlotKeyJson[list.Plots.Length];
        for (var index = 0; index < plots.Length; index++)
        {
            plots[index] = PlotKeyJson.From(list.Plots[index]);
        }

        return new SavedListJson { Id = list.Id, Name = list.Name, CreatedUtc = list.CreatedUtc, Plots = plots };
    }

    private static bool TryFromJson(SavedSearchJson? json, out SavedSearch search)
    {
        search = null!;
        if (json is not { Id: { } id, Name: { } name, World: { } world, Districts: { } districts } || id == Guid.Empty || world == 0)
        {
            return false;
        }

        var allWards = json.AllWards ?? true;
        var area = new AreaScope(
            world,
            AreaScope.DistrictMaskOf(districts),
            allWards,
            allWards ? 0 : AreaScope.WardMaskOf(json.Wards ?? []),
            json.MainDivision ?? true,
            json.Subdivision ?? true);
        var defaults = PlotFilter.Default;
        var filter = PlotFilter.Sanitized(
            json.Sizes ?? defaults.Sizes,
            json.Buyer ?? defaults.Buyer,
            json.PriceCeiling ?? 0,
            json.Statuses ?? defaults.Statuses,
            json.FirstPlot ?? defaults.FirstPlot,
            json.LastPlot ?? defaults.LastPlot,
            json.MinimumEntries,
            json.MaximumEntries,
            json.MaximumAgeHours ?? 0);
        search = new SavedSearch(id, name, area, filter, json.CreatedUtc ?? default);
        return true;
    }

    private static bool TryFromJson(SavedListJson? json, out SavedList list)
    {
        list = null!;
        if (json is not { Id: { } id, Name: { } name, Plots: { } plots } || id == Guid.Empty)
        {
            return false;
        }

        var keys = ImmutableArray.CreateBuilder<PlotKey>(plots.Length);
        for (var index = 0; index < plots.Length; index++)
        {
            if (!plots[index].TryToKey(out var key))
            {
                return false;
            }

            if (!keys.Contains(key))
            {
                keys.Add(key);
            }
        }

        list = new SavedList(id, name, keys.ToImmutable(), json.CreatedUtc ?? default);
        return true;
    }

    private static ImmutableArray<KeyValuePair<string, DateTime>> ReadDelivered(DeliveredNoticeJson[]? delivered)
    {
        if (delivered is null)
        {
            return [];
        }

        var entries = ImmutableArray.CreateBuilder<KeyValuePair<string, DateTime>>(delivered.Length);
        for (var index = 0; index < delivered.Length; index++)
        {
            if (delivered[index] is { Key: { Length: > 0 } key, AtUtc: { } at })
            {
                entries.Add(new KeyValuePair<string, DateTime>(key, at));
            }
        }

        return entries.ToImmutable();
    }

    private static ImmutableArray<PlotKey> ReadPlots(PlotKeyJson[]? plots)
    {
        if (plots is null)
        {
            return [];
        }

        var keys = ImmutableArray.CreateBuilder<PlotKey>(plots.Length);
        for (var index = 0; index < plots.Length; index++)
        {
            if (plots[index].TryToKey(out var key))
            {
                keys.Add(key);
            }
        }

        return keys.ToImmutable();
    }

    private static ImmutableArray<NoticeRecord> ReadRecent(NoticeRecordJson[]? recent)
    {
        if (recent is null)
        {
            return [];
        }

        var records = ImmutableArray.CreateBuilder<NoticeRecord>(Math.Min(recent.Length, MaximumRecent));
        for (var index = 0; index < recent.Length && records.Count < MaximumRecent; index++)
        {
            if (recent[index] is { Kind: { } kind, Plot: { } plot, AtUtc: { } at } record && Enum.IsDefined(kind) && plot.TryToKey(out var key))
            {
                records.Add(new NoticeRecord(kind, key, record.Source ?? string.Empty, at, record.PhaseEndUtc));
            }
        }

        return records.ToImmutable();
    }
}
