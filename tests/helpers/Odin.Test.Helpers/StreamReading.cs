namespace Odin.Test.Helpers;

#nullable enable

/// Test-only whole reads. Production code has none for payloads: they are unbounded, so they stream (#1892).
public static class StreamReading
{
    /// Reads to the end, and checks the stream yielded exactly the Length it promised.
    public static async Task<byte[]> ReadToEndAsync(this Stream stream, CancellationToken ct = default)
    {
        using var copy = new MemoryStream();
        await stream.CopyToAsync(copy, ct);
        if (copy.Length != stream.Length)
        {
            throw new InvalidDataException($"Stream promised {stream.Length} bytes and yielded {copy.Length}");
        }

        return copy.ToArray();
    }
}
