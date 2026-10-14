using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AutoHouseLottery.Core.Persistence;

namespace AutoHouseLottery.Core.Storage;

// A small JSON file written off the framework thread. Writes queue one behind the other and a burst of changes collapses
// into the newest one, which is serialized on the writer thread from the immutable copy the caller handed over.
internal sealed class QueuedFile
{
    private const int FlushTimeoutMs = 2_000;

    private readonly string path;
    private readonly string description;
    private readonly Lock gate = new();
    private Func<byte[]>? pending;
    private Task tail = Task.CompletedTask;

    public QueuedFile(string path, string description)
    {
        this.path = path;
        this.description = description;
    }

    public string Path => path;

    public void Write(Func<byte[]> serialize)
    {
        lock (gate)
        {
            var idle = pending is null;
            pending = serialize;
            if (idle)
            {
                tail = tail.ContinueWith(_ => Drain(), CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
            }
        }
    }

    public Task<byte[]?> ReadAsync() => Task.Run(Read);

    // Keeps an unreadable file under a dated name before any later write can replace it.
    public void BackUp(DateTime nowUtc)
    {
        lock (gate)
        {
            tail = tail.ContinueWith(_ => Copy(nowUtc), CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
        }
    }

    public void Flush()
    {
        Task current;
        lock (gate)
        {
            current = tail;
        }

        current.Wait(FlushTimeoutMs);
    }

    private byte[]? Read()
    {
        try
        {
            return File.Exists(path) ? File.ReadAllBytes(path) : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            RunLog.Warning(exception, $"The {description} file could not be read");
            return null;
        }
    }

    private void Copy(DateTime nowUtc)
    {
        var backup = string.Concat(path, ".unreadable-", nowUtc.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture));
        try
        {
            File.Copy(path, backup, overwrite: false);
            RunLog.Warning($"The unreadable {description} file was kept as {backup}.");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            RunLog.Warning(exception, $"The unreadable {description} file could not be kept as {backup}");
        }
    }

    private void Drain()
    {
        Func<byte[]>? serialize;
        lock (gate)
        {
            serialize = pending;
            pending = null;
        }

        if (serialize is null)
        {
            return;
        }

        try
        {
            var bytes = serialize();
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path) ?? string.Empty);
            JournalFile.RewriteAtomically(path, bytes);
        }
        catch (Exception exception)
        {
            RunLog.Warning(exception, $"The {description} file could not be saved");
        }
    }
}
