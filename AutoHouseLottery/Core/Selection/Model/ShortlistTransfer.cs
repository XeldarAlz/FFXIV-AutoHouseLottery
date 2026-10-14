using System.Text.Json;
using AutoHouseLottery.Core.Housing.Model;

namespace AutoHouseLottery.Core.Selection.Model;

internal static class ShortlistTransfer
{
    public const int Schema = 1;

    public static string Export(Shortlist shortlist)
    {
        var plots = new PlotKeyJson[shortlist.Count];
        for (var index = 0; index < plots.Length; index++)
        {
            plots[index] = PlotKeyJson.From(shortlist.Plots[index]);
        }

        var document = new ShortlistJson { Schema = Schema, Id = shortlist.Id, Name = shortlist.Name, Plots = plots };
        return JsonSerializer.Serialize(document, ShortlistJsonContext.Default.ShortlistJson);
    }

    public static ShortlistImportError TryImport(string json, out Shortlist? shortlist)
    {
        shortlist = null;
        ShortlistJson document;
        try
        {
            document = JsonSerializer.Deserialize(json, ShortlistJsonContext.Default.ShortlistJson);
        }
        catch (JsonException)
        {
            return ShortlistImportError.Malformed;
        }

        if (document.Schema != Schema)
        {
            return ShortlistImportError.UnsupportedSchema;
        }

        if (document.Id is not { } id || document.Name is not { } name || document.Plots is not { } plots)
        {
            return ShortlistImportError.MissingField;
        }

        var keys = new PlotKey[plots.Length];
        for (var index = 0; index < plots.Length; index++)
        {
            if (!plots[index].TryToKey(out keys[index]))
            {
                return ShortlistImportError.InvalidPlot;
            }
        }

        var imported = new Shortlist(id, name);
        imported.AddRange(keys);
        shortlist = imported;
        return ShortlistImportError.None;
    }
}
