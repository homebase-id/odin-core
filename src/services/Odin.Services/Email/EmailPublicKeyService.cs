using System;
using System.Threading.Tasks;
using Odin.Core.Cryptography.Pgp;
using Odin.Core.Exceptions;
using Odin.Core.Storage.Database.Identity;
using Odin.Core.Storage.Database.Identity.Wrappers;
using Odin.Core.Time;
using Odin.Services.Base;

namespace Odin.Services.Email;

#nullable enable

/// <summary>
/// The tenant's published E2E email PUBLIC key (docs/email-keys-plan.md). Written at
/// email activation, read by the anonymous publication surfaces: WKD serves the binary
/// certificate, the DID document derives its keyAgreement JWK from it, and the mail
/// server receives it for encryption-at-rest. Presence doubles as the per-tenant
/// "email activated" signal for those surfaces.
///
/// Public material only - the secret keyring lives on the owner-locked email drive
/// and never passes through here.
/// </summary>
public class EmailPublicKeyService(IdentityDatabase identityDatabase)
{
    private const string ContextKey = "8c2f9b41-6e07-4b6a-9d35-27a51c04d1b8";

    private static readonly SingleKeyValueStorage Storage =
        TenantSystemStorage.CreateSingleKeyValueStorage(Guid.Parse(ContextKey));

    private static readonly Guid PublishedKeyRecordKey = Guid.Parse("f4d21e83-90ab-4c47-8bd1-53e70a2c96e5");

    public async Task<PublishedEmailPublicKey?> GetPublishedKeyAsync()
    {
        return await Storage.GetAsync<PublishedEmailPublicKey>(identityDatabase.KeyValueCached, PublishedKeyRecordKey);
    }

    /// <summary>
    /// Publishes (or on rotation: replaces) the tenant's public certificate. Validates
    /// that it parses and carries an encryption subkey before anything is stored.
    /// </summary>
    public async Task PublishAsync(string publicCertificateArmored)
    {
        string fingerprint;
        try
        {
            // Also throws when there is no encryption subkey - nothing unpublishable gets stored
            OpenPgpKeyManagement.GetEncryptionSubkeySpkiDer(publicCertificateArmored);
            fingerprint = OpenPgpKeyManagement.GetFingerprintHex(publicCertificateArmored);
        }
        catch (Exception e)
        {
            throw new OdinSystemException("Not a publishable OpenPGP certificate", e);
        }

        var record = new PublishedEmailPublicKey
        {
            PublicCertificateArmored = publicCertificateArmored,
            FingerprintHex = fingerprint,
            PublishedAt = UnixTimeUtc.Now(),
        };

        await Storage.UpsertAsync(identityDatabase.KeyValueCached, PublishedKeyRecordKey, record);
    }

    public async Task UnpublishAsync()
    {
        await Storage.DeleteAsync(identityDatabase.KeyValueCached, PublishedKeyRecordKey);
    }
}

public class PublishedEmailPublicKey
{
    public string PublicCertificateArmored { get; init; } = "";
    public string FingerprintHex { get; init; } = "";
    public UnixTimeUtc PublishedAt { get; init; }
}
