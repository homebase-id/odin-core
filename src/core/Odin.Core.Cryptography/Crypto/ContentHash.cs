using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Blake3;

namespace Odin.Core.Cryptography.Crypto;

/// <summary>
/// The hash algorithms a client may choose for a payload hash. The numbers are wire contract.
/// </summary>
public enum ContentHashAlgorithm
{
    Sha256 = 1,
    Blake3 = 2
}

/// <summary>
/// Incremental SHA-256 or BLAKE3 (256-bit, standard mode) over data that arrives in pieces.
/// BLAKE3 is Blake3.NET (managed SIMD); BouncyCastle's Blake3Digest was measured ~15x slower and slower than SHA-256.
/// </summary>
public sealed class IncrementalContentHash : IDisposable
{
    public const int HashLength = 32;

    private readonly IncrementalHash _sha256;
    private readonly Hasher _blake3;

    public IncrementalContentHash(ContentHashAlgorithm algorithm)
    {
        switch (algorithm)
        {
            case ContentHashAlgorithm.Sha256:
                _sha256 = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                break;
            case ContentHashAlgorithm.Blake3:
                _blake3 = Hasher.New();
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(algorithm), algorithm, "Unknown content hash algorithm");
        }
    }

    public void Append(ReadOnlySpan<byte> data)
    {
        if (_sha256 != null)
        {
            _sha256.AppendData(data);
        }
        else
        {
            _blake3.Update(data);
        }
    }

    public byte[] GetHash()
    {
        if (_sha256 != null)
        {
            return _sha256.GetHashAndReset();
        }

        var hash = new byte[HashLength];
        _blake3.Finalize(hash);
        return hash;
    }

    public static byte[] Compute(ContentHashAlgorithm algorithm, ReadOnlySpan<byte> data)
    {
        using var hash = new IncrementalContentHash(algorithm);
        hash.Append(data);
        return hash.GetHash();
    }

    public void Dispose()
    {
        _sha256?.Dispose();
        _blake3?.Dispose();
    }
}

/// <summary>
/// Forward-only read stream that hashes every byte read through it. It does not own the inner stream.
/// Length is forwarded because writers compare bytes written against it.
/// </summary>
public sealed class HashingReadStream(Stream inner, ContentHashAlgorithm algorithm) : Stream
{
    private readonly IncrementalContentHash _hash = new(algorithm);
    private long _bytesRead;

    /// <summary>
    /// The hash of every byte read so far. Call once, after the stream has been read to the end.
    /// </summary>
    public byte[] GetHash() => _hash.GetHash();

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => inner.Length;

    public override long Position
    {
        get => _bytesRead;
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer)
    {
        var n = inner.Read(buffer);
        Hash(buffer[..n]);
        return n;
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        => ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        var n = await inner.ReadAsync(buffer, cancellationToken);
        Hash(buffer.Span[..n]);
        return n;
    }

    private void Hash(ReadOnlySpan<byte> data)
    {
        _hash.Append(data);
        _bytesRead += data.Length;
    }

    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _hash.Dispose();
        }

        base.Dispose(disposing);
    }
}
