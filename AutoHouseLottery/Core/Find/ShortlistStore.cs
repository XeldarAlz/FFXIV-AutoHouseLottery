using System.IO;
using System.Text;
using System.Threading.Tasks;
using AutoHouseLottery.Core.Selection.Model;
using AutoHouseLottery.Core.Storage;

namespace AutoHouseLottery.Core.Find;

// The working shortlist in shortlist.json, in the shortlist exchange format, read and written off the framework thread.
internal sealed class ShortlistStore
{
    private const string FileName = "shortlist.json";

    private readonly QueuedFile file;
    private readonly Task<byte[]?> loading;

    public ShortlistStore(string directory)
    {
        file = new QueuedFile(Path.Combine(directory, FileName), "shortlist");
        loading = file.ReadAsync();
    }

    public bool TryTakeLoaded(out Shortlist? loaded)
    {
        loaded = null;
        if (!loading.IsCompleted)
        {
            return false;
        }

        if (loading.Result is not { } bytes)
        {
            return true;
        }

        var error = ShortlistTransfer.TryImport(Encoding.UTF8.GetString(bytes), out loaded);
        if (error != ShortlistImportError.None)
        {
            RunLog.Warning($"The saved shortlist could not be read ({error}); starting with an empty shortlist.");
        }

        return true;
    }

    public void Save(ShortlistSnapshot snapshot, string name)
    {
        file.Write(() =>
        {
            var copy = new Shortlist(snapshot.Id, name);
            copy.AddRange(snapshot.Plots.AsSpan());
            return Encoding.UTF8.GetBytes(ShortlistTransfer.Export(copy));
        });
    }

    public void Flush() => file.Flush();
}
