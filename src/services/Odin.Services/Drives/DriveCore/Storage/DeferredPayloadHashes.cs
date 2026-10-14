using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Odin.Core.Cryptography.Crypto;
using Odin.Core.Exceptions;
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
    /// <summary>
    /// A payloadHash part is a few hundred bytes: two base64 hashes
    /// </summary>
    private const int MaxPartBytes = 1024;

    /// <summary>
    /// Deferred hashes still waiting for their payloadHash part, with the hash of the bytes as written
    /// </summary>
    private readonly Dictionary<string, (PayloadHash Hash, byte[] Received)> _pending = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// After a payload is written: verifies its stored hash, or holds it when the hash is deferred
    /// </summary>
    public void Written(string payloadKey, PayloadHash hash, HashingReadStream written)
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

        _pending[payloadKey] = (hash, written.GetHash());
    }

    /// <summary>
    /// A payloadHash part: the values of a deferred hash, after its payload
    /// </summary>
    public async Task ResolveAsync(string payloadKey, Stream body)
    {
        // Also misses a part before its payload, a second one, and one for a payload whose hash was not deferred
        if (!_pending.Remove(payloadKey ?? "", out var pending))
        {
            throw PayloadHash.Invalid(payloadKey,
                "a payloadHash part comes once, after its payload, and only for a payload whose hash is deferred");
        }

        PayloadHashValues values;
        try
        {
            values = await BoundedRead.DeserializeAsync<PayloadHashValues>(body, MaxPartBytes);
        }
        catch (JsonException e)
        {
            throw PayloadHash.Invalid(payloadKey, $"the payloadHash part is not valid JSON: {e.Message}");
        }

        pending.Hash.Resolve(payloadKey, values?.StoredHash, values?.ContentHash);
        pending.Hash.AssertStoredHashMatches(payloadKey, pending.Received);
    }

    /// <summary>
    /// When the request has ended: a deferred hash without its payloadHash part fails the upload. That is also how a
    /// client aborts one, e.g. when it finds the source changed while it was sending it.
    /// </summary>
    public void AssertNoneMissing()
    {
        if (_pending.Count > 0)
        {
            throw new OdinClientException(
                $"Payload {string.Join(", ", _pending.Keys)}: the deferred hash was not followed by a payloadHash part",
                OdinClientErrorCode.PayloadHashMissing);
        }
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
