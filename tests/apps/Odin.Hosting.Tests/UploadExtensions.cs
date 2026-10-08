using System.Collections.Generic;
using System.Linq;
using Odin.Core.Cryptography.Crypto;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.FileSystem.Base.Upload;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Drive;

namespace Odin.Hosting.Tests;

public static class UploadExtensions
{
    public static IEnumerable<UploadManifestPayloadDescriptor> ToPayloadDescriptorList(this IEnumerable<TestPayloadDefinition> list)
    {
        return list.Select(tpd => tpd.ToPayloadDescriptor());
    }

    /// <summary>
    /// What a client sends for an unencrypted payload (#1895): storedHash and contentHash are the same hash
    /// </summary>
    public static TestPayloadDefinition WithHash(this TestPayloadDefinition payload, ContentHashAlgorithm algorithm)
    {
        var hash = IncrementalContentHash.Compute(algorithm, payload.Content);
        payload.Hash = new PayloadHash { Algorithm = algorithm, StoredHash = hash, ContentHash = (byte[])hash.Clone() };
        return payload;
    }

    /// <summary>
    /// What a client sends for an encrypted payload (#1895): storedHash over the ciphertext it uploads, contentHash
    /// the plaintext hash encrypted with the file key under <see cref="PayloadHash.ContentHashIv"/> of the payload IV
    /// </summary>
    public static TestPayloadDefinition WithEncryptedHash(this TestPayloadDefinition payload, byte[] ciphertext,
        byte[] fileAesKey, ContentHashAlgorithm algorithm)
    {
        payload.Hash = new PayloadHash
        {
            Algorithm = algorithm,
            StoredHash = IncrementalContentHash.Compute(algorithm, ciphertext),
            ContentHash = AesCbc.Encrypt(IncrementalContentHash.Compute(algorithm, payload.Content), fileAesKey,
                PayloadHash.ContentHashIv(payload.Iv))
        };
        return payload;
    }

    public static string GetFilename(this ThumbnailDescriptor descriptor, string payloadKey = WebScaffold.PAYLOAD_KEY)
    {
        return $"{descriptor.PixelWidth}x{descriptor.PixelHeight}-{payloadKey}";
    }
}