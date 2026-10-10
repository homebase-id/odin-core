using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Odin.Core.Exceptions;

namespace Odin.Core.Util;

/// <summary>
/// Reads of request parts that have to be held whole -- the JSON sections of a multipart upload -- under a bound the
/// server sets, not the sender. Kestrel does not bound the request body (<c>MaxRequestBodySize = null</c>, for
/// payloads), so without this a sender decides how much memory a section takes (#1892).
/// </summary>
public static class BoundedRead
{
    /// <summary>
    /// Bound on one JSON section (metadata, instructions, key header). Real ones are far smaller: a header's metadata
    /// is capped at 60,000 characters in the database.
    /// </summary>
    public const int MaxJsonSectionBytes = 4 * 1024 * 1024;

    /// <summary>
    /// Reads <paramref name="stream"/> to the end, refusing it with a 400 as soon as it passes
    /// <paramref name="maxBytes"/>, so it never buffers more than that.
    /// </summary>
    public static async Task<byte[]> ReadAllBytesAsync(Stream stream, int maxBytes = MaxJsonSectionBytes,
        CancellationToken cancellationToken = default)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[16 * 1024];
        int n;
        while ((n = await stream.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + n > maxBytes)
            {
                throw new OdinClientException($"Request part exceeds the {maxBytes} byte limit",
                    OdinClientErrorCode.MaxContentLengthExceeded);
            }

            buffer.Write(chunk, 0, n);
        }

        return buffer.ToArray();
    }

    /// <summary>
    /// <see cref="ReadAllBytesAsync"/>, decoded the way <see cref="StreamReader"/> decodes (UTF-8, honouring a BOM).
    /// </summary>
    public static async Task<string> ReadAllTextAsync(Stream stream, int maxBytes = MaxJsonSectionBytes,
        CancellationToken cancellationToken = default)
    {
        var bytes = await ReadAllBytesAsync(stream, maxBytes, cancellationToken);
        using var reader = new StreamReader(new MemoryStream(bytes));
        return await reader.ReadToEndAsync(cancellationToken);
    }
}
