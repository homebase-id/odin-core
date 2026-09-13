using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Odin.Core;
using Odin.Core.Cryptography.Crypto;
using Odin.Core.Exceptions;
using Odin.Core.Identity;
using Odin.Core.Storage.Database.System.Table;

namespace Odin.Services.Email.Dkim;

#nullable enable

/// <summary>
/// Server-operational storage for tenant DKIM signing keys - the CertificateStore
/// pattern applied to DKIM (docs/email-keys-plan.md custody table): the public key
/// is stored in cleartext (it is published in DNS anyway), the private key is
/// AES-CBC encrypted under the dedicated <c>Email:DkimStorageKey</c> config key.
/// </summary>
public interface IDkimStore
{
    /// <summary>
    /// False when Email:DkimStorageKey is absent - Save/Get then throw loudly.
    /// Unattended callers (status, deletion ride-alongs) check this first.
    /// </summary>
    bool IsConfigured { get; }

    /// <summary>Upserts the domain's selector set (activation and rotation).</summary>
    Task SaveKeysAsync(string domain, IReadOnlyCollection<DkimKey> keys);

    /// <summary>
    /// The domain's keys with decrypted, pair-verified private halves.
    /// Empty list when the domain has no keys (email not activated).
    /// </summary>
    Task<List<DkimKey>> GetKeysAsync(string domain);

    /// <summary>Removes all of the domain's keys (tenant deletion ride-along).</summary>
    Task DeleteKeysAsync(string domain);
}

public class DkimStore(
    IServiceProvider serviceProvider,
    DkimStorageKey dkimStorageKey) : IDkimStore
{
    private readonly byte[] _storageKey = dkimStorageKey.StorageKey;

    public bool IsConfigured => _storageKey.Length > 0;

    // Signed on save, verified on load: proves the decrypted private key still
    // pairs with the stored public key (the CertificateStore ThrowIfBadCertificate
    // equivalent - AesCbc has no MAC, so integrity comes from the pair proof).
    private static readonly byte[] PairProofVector = "odin dkim pair proof"u8.ToArray();

    public async Task SaveKeysAsync(string domain, IReadOnlyCollection<DkimKey> keys)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(domain, nameof(domain));
        ArgumentNullException.ThrowIfNull(keys, nameof(keys));
        ThrowIfNotConfigured();

        var odinId = new OdinId(domain);

        using var scope = serviceProvider.CreateScope();
        var table = scope.ServiceProvider.GetRequiredService<TableDkimKeys>();

        foreach (var key in keys)
        {
            var iv = IvFromPublicKey(key.PublicKeyBase64);
            var encryptedPrivateKey = Convert.ToHexString(AesCbc.Encrypt(key.PrivateKeyPkcs8, _storageKey, iv));

            await table.UpsertAsync(new DkimKeysRecord
            {
                domain = odinId,
                selector = key.Selector,
                algorithm = key.KTag,
                publicKey = key.PublicKeyBase64,
                privateKey = encryptedPrivateKey,
            });
        }
    }

    public async Task<List<DkimKey>> GetKeysAsync(string domain)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(domain, nameof(domain));
        ThrowIfNotConfigured();

        var odinId = new OdinId(domain);

        using var scope = serviceProvider.CreateScope();
        var table = scope.ServiceProvider.GetRequiredService<TableDkimKeys>();

        var records = await table.GetByDomainAsync(odinId);

        var result = new List<DkimKey>(records.Count);
        foreach (var record in records)
        {
            var iv = IvFromPublicKey(record.publicKey);
            var privateKeyPkcs8 = AesCbc.Decrypt(Convert.FromHexString(record.privateKey), _storageKey, iv);

            var key = new DkimKey
            {
                Selector = record.selector,
                Algorithm = DkimKey.AlgorithmFromKTag(record.algorithm),
                PublicKey = Convert.FromBase64String(record.publicKey),
                PrivateKeyPkcs8 = privateKeyPkcs8,
            };

            ThrowIfBadKeyPair(domain, key);
            result.Add(key);
        }

        return result;
    }

    public async Task DeleteKeysAsync(string domain)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(domain, nameof(domain));

        var odinId = new OdinId(domain);

        using var scope = serviceProvider.CreateScope();
        var table = scope.ServiceProvider.GetRequiredService<TableDkimKeys>();

        await table.DeleteByDomainAsync(odinId);
    }

    //

    private void ThrowIfNotConfigured()
    {
        if (_storageKey.Length == 0)
        {
            throw new OdinSystemException("Email:DkimStorageKey is not configured; cannot store or read DKIM keys");
        }
    }

    private static void ThrowIfBadKeyPair(string domain, DkimKey key)
    {
        var signature = DkimKeyGenerator.Sign(key, PairProofVector);
        if (!DkimKeyGenerator.Verify(key.Algorithm, key.PublicKey, PairProofVector, signature))
        {
            throw new OdinSystemException(
                $"DKIM key '{key.Selector}' for {domain} failed the pair proof; stored key material is corrupt");
        }
    }

    // The IV must be derivable on read, so it is seeded from the cleartext-stored
    // public key - unique per keypair, which is what keeps CBC IV reuse away
    // (rotation replaces both halves, so a row never sees two plaintexts under one IV).
    private static byte[] IvFromPublicKey(string publicKeyBase64)
    {
        ArgumentException.ThrowIfNullOrEmpty(publicKeyBase64, nameof(publicKeyBase64));

        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(publicKeyBase64));

        var iv = new byte[16];
        Array.Copy(hashBytes, 0, iv, 0, 16);

        return iv;
    }
}

public class DkimStorageKey(byte[] storageKey)
{
    public byte[] StorageKey { get; } = storageKey;
}
