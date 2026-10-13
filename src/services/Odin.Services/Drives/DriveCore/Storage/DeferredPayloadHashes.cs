using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Odin.Core.Cryptography.Crypto;
using Odin.Core.Exceptions;
using Odin.Core.Time;
using Odin.Core.Util;

namespace Odin.Services.Drives.DriveCore.Storage;

/// <summary>
/// Checks each payload's stored hash once its bytes are written, for the upload, update and add-payload writers.
/// A deferred hash (#1935, see <see cref="PayloadHash.DeferredSentinel"/>) is instead held, with the hash of the bytes
/// as written, until its payloadHash part arrives. A client that encrypts while it sends only knows the stored hash
/// then, so it doesn't have to read and encrypt the source twice. One instance per upload request.
/// </summary>
public class DeferredPayloadHashes
{
    private readonly Dictionary<string, Deferred> _deferred = new(StringComparer.InvariantCultureIgnoreCase);

    /// <summary>
    /// After a payload is written: verifies its stored hash, or holds it when the hash is deferred
    /// </summary>
    public void Written(string payloadKey, UnixTimeUtcUnique payloadUid, PayloadHash hash, HashingReadStream written)
    {
        if (hash == null)
        {
            return;
        }

        if (!hash.IsDeferred())
        {
            hash.AssertStoredHashMatches(payloadKey, written);
            return;
        }

        _deferred[payloadKey] = new Deferred(hash, written.GetHash(), payloadUid);
    }

    /// <summary>
    /// A payloadHash part: the values of a deferred hash, after its payload
    /// </summary>
    public async Task ResolveAsync(string payloadKey, Stream body)
    {
        if (!_deferred.TryGetValue(payloadKey ?? "", out var deferred) || deferred.Resolved)
        {
            throw PayloadHash.Invalid(payloadKey,
                "a payloadHash part comes once, after its payload, and only for a payload whose hash is deferred");
        }

        var values = await BoundedRead.DeserializeAsync<PayloadHashValues>(body);
        deferred.Hash.Resolve(payloadKey, values?.StoredHash, values?.ContentHash);
        deferred.Resolved = true;
        deferred.Hash.AssertStoredHashMatches(payloadKey, deferred.Received);
    }

    /// <summary>
    /// When the request has ended: a deferred hash without its payloadHash part fails the upload. That is also how a
    /// client aborts one, e.g. when it finds the source changed while it was sending it.
    /// </summary>
    public void AssertNoneMissing()
    {
        var missing = _deferred.Where(d => !d.Value.Resolved).Select(d => d.Key).ToList();
        if (missing.Count > 0)
        {
            throw new OdinClientException(
                $"Payload {string.Join(", ", missing)}: the deferred hash was not followed by a payloadHash part",
                OdinClientErrorCode.PayloadHashMissing);
        }
    }

    /// <summary>
    /// Every payload with a deferred hash, for cleanup of staged bytes that are not in the package
    /// </summary>
    public IEnumerable<PayloadDescriptor> Staged =>
        _deferred.Select(d => new PayloadDescriptor { Key = d.Key, Uid = d.Value.PayloadUid });

    private sealed class Deferred(PayloadHash hash, byte[] received, UnixTimeUtcUnique payloadUid)
    {
        public PayloadHash Hash { get; } = hash;
        public byte[] Received { get; } = received;
        public UnixTimeUtcUnique PayloadUid { get; } = payloadUid;
        public bool Resolved { get; set; }
    }

    /// <summary>
    /// The body of a payloadHash part
    /// </summary>
    public class PayloadHashValues
    {
        public byte[] StoredHash { get; set; }
        public byte[] ContentHash { get; set; }
    }
}
