using System.Collections.Immutable;
using System.Globalization;
using System.IO;

namespace AutoHouseLottery.Core.Persistence;

internal static class JournalFile
{
    public const byte LineFeed = (byte)'\n';

    private const int BufferSize = 4096;
    private const string TemporarySuffix = ".tmp";

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

    public static void AppendDurably(string path, ReadOnlySpan<byte> bytes)
    {
        using var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read, BufferSize, FileOptions.WriteThrough);
        stream.Write(bytes);
        stream.Flush(flushToDisk: true);
    }

    public static void RewriteAtomically(string path, ReadOnlySpan<byte> bytes)
    {
        var temporaryPath = path + TemporarySuffix;
        using (var stream = new FileStream(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None, BufferSize, FileOptions.WriteThrough))
        {
            stream.Write(bytes);
            stream.Flush(flushToDisk: true);
        }

        File.Move(temporaryPath, path, overwrite: true);
    }

    public static void TruncateDurably(string path, long length)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.Read, BufferSize, FileOptions.WriteThrough);
        stream.SetLength(length);
        stream.Flush(flushToDisk: true);
    }

    public static string BackUp(string path, int schema, DateTime nowUtc)
    {
        var backupPath = string.Create(CultureInfo.InvariantCulture, $"{path}.schema{schema}-{nowUtc:yyyyMMdd'T'HHmmss'Z'}.bak");
        File.Copy(path, backupPath, overwrite: false);
        return backupPath;
    }
}
