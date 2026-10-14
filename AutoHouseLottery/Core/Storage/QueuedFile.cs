using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AutoHouseLottery.Core.Persistence;

namespace AutoHouseLottery.Core.Storage;

// A small JSON file written off the framework thread. Writes queue one behind the other and a burst of changes collapses
// into the newest one, which is serialized on the writer thread from the immutable copy the caller handed over. A read
// tells a missing file apart from one that could not be read, so a locked file is never mistaken for an empty one.
internal sealed class QueuedFile
{
    private const int FlushTimeoutMs = 2_000;
    private const int FirstRetrySeconds = 5;
    private const int LastRetryMinutes = 5;
    private const string BackupStampFormat = "yyyyMMdd-HHmmss";

    private static readonly TimeSpan firstRetry = TimeSpan.FromSeconds(FirstRetrySeconds);
    private static readonly TimeSpan lastRetry = TimeSpan.FromMinutes(LastRetryMinutes);

    private readonly string path;
    private readonly string description;
    private readonly Action<Exception, string> report;
    private readonly Lock gate = new();
    private readonly CancellationTokenSource closing = new();
    private Func<byte[]>? pending;
    private Task tail = Task.CompletedTask;

    public QueuedFile(string path, string description, Action<Exception, string> report)
    {
        this.path = path;
        this.description = description;
        this.report = report;
    }

    public void Write(Func<byte[]> serialize)
    {
        lock (gate)
        {
            var idle = pending is null;
            pending = serialize;
            if (idle)
            {
                Enqueue(Drain);
            }
        }
    }

    public FileRead Read()
    {
        try
        {
            return File.Exists(path) ? new FileRead(FileReadStatus.Read, File.ReadAllBytes(path)) : new FileRead(FileReadStatus.Missing, null);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            report(exception, $"The {description} file could not be read");
            return new FileRead(FileReadStatus.Failed, null);
        }
    }

    // Reads until the file is there and readable or certainly missing; a file that is locked or failing for now is tried
    // again with growing pauses instead of being taken for an empty one. Ends with Failed only when the file is closed.
    public async Task<FileRead> ReadWhenReadableAsync()
    {
        var delay = firstRetry;
        while (true)
        {
            var read = await Task.Run(Read).ConfigureAwait(false);
            if (read.Status != FileReadStatus.Failed)
            {
                return read;
            }

            try
            {
                await Task.Delay(delay, closing.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return read;
            }

            delay = delay * 2 < lastRetry ? delay * 2 : lastRetry;
        }
    }

    // Keeps a damaged file under a dated name before any later write can replace it.
    public void BackUp(DateTime nowUtc)
    {
        lock (gate)
        {
            Enqueue(() => Copy(nowUtc));
        }
    }

    public void Close()
    {
        closing.Cancel();
        Task current;
        lock (gate)
        {
            current = tail;
        }

        current.Wait(FlushTimeoutMs);
    }

    private void Enqueue(Action work) => tail = tail.ContinueWith(_ => work(), CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);

    private void Copy(DateTime nowUtc)
    {
        var backup = string.Concat(path, ".unreadable-", nowUtc.ToString(BackupStampFormat, CultureInfo.InvariantCulture));
        try
        {
            File.Copy(path, backup, overwrite: false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            report(exception, $"The unreadable {description} file could not be kept as {backup}");
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
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? string.Empty);
            JournalFile.RewriteAtomically(path, bytes);
        }
        catch (Exception exception)
        {
            report(exception, $"The {description} file could not be saved");
        }
    }
}
