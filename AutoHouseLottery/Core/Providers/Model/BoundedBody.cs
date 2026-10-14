using System.Buffers;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace AutoHouseLottery.Core.Providers.Model;

internal static class BoundedBody
{
    private const int ChunkSize = 81_920;
    private const int DefaultCapacity = 65_536;

    // Null when the body is larger than the limit, whether it says so up front or only while streaming.
    public static async Task<byte[]?> ReadAsync(Stream stream, long? declaredLength, long limit, CancellationToken token)
    {
        if (declaredLength > limit)
        {
            return null;
        }

        var capacity = declaredLength is { } length && length > 0 ? (int)length : DefaultCapacity;
        using var buffer = new MemoryStream(capacity);
        var chunk = ArrayPool<byte>.Shared.Rent(ChunkSize);
        try
        {
            int read;
            while ((read = await stream.ReadAsync(chunk.AsMemory(0, ChunkSize), token).ConfigureAwait(false)) > 0)
            {
                if (buffer.Length + read > limit)
                {
                    return null;
                }

                buffer.Write(chunk, 0, read);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(chunk);
        }

        return buffer.ToArray();
    }
}
