using System;
using Odin.Core.Exceptions;

namespace Odin.Services.Drives.FileSystem.Base;

/// <summary>
/// A byte range of a payload. A null <see cref="Length"/> means "to the end".
/// </summary>
public class FileChunk
{
    public Int64 Start { get; set; }
    public Int64? Length { get; set; }

    /// <summary>
    /// This range against a payload of <paramref name="size"/> bytes, per RFC 9110 14.1.2: one running past the end
    /// is clamped to it, one starting at or past the end is unsatisfiable. The result's Length is exact.
    /// </summary>
    public FileChunk ResolveAgainst(Int64 size)
    {
        if (Start < 0 || Length < 1)
        {
            throw new OdinClientException($"Invalid byte range: start={Start}, length={Length}");
        }

        if (Start >= size)
        {
            throw new OdinRangeNotSatisfiableException(size, $"Range start {Start} >= payload size {size}");
        }

        var available = size - Start;
        return new FileChunk { Start = Start, Length = Length == null ? available : Math.Min(Length.Value, available) };
    }
}
