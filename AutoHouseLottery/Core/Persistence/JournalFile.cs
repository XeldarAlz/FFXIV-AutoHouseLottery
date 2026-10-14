using System.Collections.Immutable;
using System.Globalization;
using System.IO;

namespace AutoHouseLottery.Core.Persistence;

internal static class JournalFile
{
    public const byte LineFeed = (byte)'\n';

    private const int UnbufferedSize = 0;
    private const string TemporarySuffix = ".tmp";
    private const string LockSuffix = ".lock";

    public static JournalLines Read(string path)
    {
        var content = File.ReadAllBytes(path);
        var lines = ImmutableArray.CreateBuilder<Range>();
        var lineStart = 0;
        for (var index = 0; index < content.Length; index++)
        {
            if (content[index] != LineFeed)
            {
                continue;
            }

            if (index > lineStart)
            {
                lines.Add(new Range(lineStart, index));
            }

            lineStart = index + 1;
        }

        return new JournalLines(content, lines.ToImmutable(), lineStart, lineStart < content.Length);
    }

    public static FileStream AcquireLock(string path)
    {
        return new FileStream(path + LockSuffix, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    }

    public static long AppendDurably(string path, long committedLength, ReadOnlySpan<byte> bytes)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.Read, UnbufferedSize, FileOptions.WriteThrough);
        try
        {
            if (stream.Length != committedLength)
            {
                stream.SetLength(committedLength);
            }

            stream.Seek(committedLength, SeekOrigin.Begin);
            stream.Write(bytes);
            stream.Flush(flushToDisk: true);
            return stream.Length;
        }
        catch (IOException)
        {
            TryTruncate(stream, committedLength);
            throw;
        }
    }

    public static void RewriteAtomically(string path, ReadOnlySpan<byte> bytes)
    {
        var temporaryPath = path + TemporarySuffix;
        WriteNewFile(temporaryPath, bytes, FileMode.Create);
        File.Move(temporaryPath, path, overwrite: true);
    }

    public static void TruncateDurably(string path, long length)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.Read, UnbufferedSize, FileOptions.WriteThrough);
        stream.SetLength(length);
        stream.Flush(flushToDisk: true);
    }

    public static string BackUp(string path, int schema, DateTime nowUtc)
    {
        var backupPath = string.Create(CultureInfo.InvariantCulture, $"{path}.schema{schema}-{nowUtc:yyyyMMdd'T'HHmmss'Z'}.bak");
        WriteNewFile(backupPath, File.ReadAllBytes(path), FileMode.CreateNew);
        return backupPath;
    }

    private static void WriteNewFile(string path, ReadOnlySpan<byte> bytes, FileMode mode)
    {
        using var stream = new FileStream(path, mode, FileAccess.Write, FileShare.None, UnbufferedSize, FileOptions.WriteThrough);
        stream.Write(bytes);
        stream.Flush(flushToDisk: true);
    }

    private static bool TryTruncate(FileStream stream, long length)
    {
        try
        {
            stream.SetLength(length);
            stream.Flush(flushToDisk: true);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
    }
}
