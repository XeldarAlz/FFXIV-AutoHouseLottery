using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AutoHouseLottery.Core.Batch.Model;
using AutoHouseLottery.Core.Persistence.Model;

namespace AutoHouseLottery.Core.Persistence;

// The batch checkpoint in batch.json. Writes go one behind the other off the framework thread and each reports whether it
// reached the disk, so a switch waits for the checkpoint it depends on. Saving nothing deletes the file.
internal sealed class BatchCheckpointFile(string path, Action<Exception, string> report)
{
    public const string FileName = "batch.json";

    private readonly Lock gate = new();
    private Task<bool> tail = Task.FromResult(true);

    // Read once at load. Missing is fine; a damaged file is kept under a dated name and the batch it held is dropped.
    public BatchCheckpoint? Load(DateTime nowUtc)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            if (BatchCodec.TryDeserialize(File.ReadAllBytes(path), out var checkpoint))
            {
                return checkpoint;
            }

            var backup = JournalFile.BackUp(path, BatchCodec.Schema, nowUtc);
            File.Delete(path);
            report(new InvalidDataException("The batch checkpoint is incomplete or from another version."), $"The batch checkpoint was kept as {backup} and dropped");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            report(exception, $"The batch checkpoint {path} could not be read; the batch it held is dropped");
        }

        return null;
    }

    // The last write queued, which every earlier one precedes; an unload waits for it.
    public Task Pending
    {
        get
        {
            lock (gate)
            {
                return tail;
            }
        }
    }

    public Task<bool> Save(BatchCheckpoint? checkpoint)
    {
        lock (gate)
        {
            tail = tail.ContinueWith(_ => Write(checkpoint), CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
            return tail;
        }
    }

    private bool Write(BatchCheckpoint? checkpoint)
    {
        try
        {
            if (checkpoint is null)
            {
                File.Delete(path);
                return true;
            }

            JournalFile.RewriteAtomically(path, BatchCodec.Serialize(checkpoint));
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            report(exception, $"The batch checkpoint {path} could not be saved");
            return false;
        }
    }
}
