#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Odin.Core;
using Odin.Core.Cryptography.Crypto;
using Odin.Core.Cryptography.Data;
using Odin.Core.Time;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Drives;
using Odin.Services.EncryptionKeyService;

namespace Odin.Services.Membership.Connections;

/// <summary>
/// A circle grant deposited into a <see cref="PeerKeyStore"/> via its write-only public key,
/// awaiting conversion into a normal Peer-Key-encrypted <see cref="CircleGrant"/> the next
/// time the store's key store key is in scope (peer CAT auth or owner online). Metadata is
/// stored in clear so the server can validate at deposit and conversion time; only the drive
/// storage keys are sealed.
/// </summary>
public class DepositedGrant
{
    public GuidId CircleId { get; set; } = null!;

    /// <summary>Provenance: the app whose client deposited this grant (audit/revoke-on-app-delete).</summary>
    public Guid? DepositingAppId { get; set; }

    public UnixTimeUtc Deposited { get; set; }

    /// <summary>The circle's permission keys at deposit time (clear, advisory - the
    /// converter re-mints from the current circle definition).</summary>
    public PermissionSet? PermissionSet { get; set; }

    public List<DepositedDriveGrant> DriveGrants { get; set; } = new();
}

public class DepositedDriveGrant
{
    public Guid DriveId { get; set; }

    public PermissionedDrive PermissionedDrive { get; set; } = null!;

    /// <summary>The drive's storage key sealed to the store's write-only public key.
    /// Null for permission-only entries (write-only drives).</summary>
    public EccEncryptedPayload? SealedStorageKey { get; set; }
}

/// <summary>
/// The write-without-read primitive for a <see cref="PeerKeyStore"/>: an ECC-384 keypair whose
/// private key is escrowed under the store's key store key (the Peer Key). Anyone holding the
/// public key can seal a deposit TO the store; only a Peer-Key holder can unseal and convert.
/// </summary>
public static class PeerKeyStoreWriteOnlyKey
{
    // Effectively non-expiring; EccFullKeyData requires a lifespan.
    private const int LifetimeHours = 24 * 365 * 50;

    /// <summary>Creates the store's keypair, escrowing the private key under the key store key.</summary>
    public static EccFullKeyData CreateKeyPair(SensitiveByteArray keyStoreKey)
    {
        return new EccFullKeyData(keyStoreKey, EccKeySize.P384, LifetimeHours);
    }

    /// <summary>
    /// ECIES-seals a payload to the store's public key using a fresh throwaway ECC-384 key;
    /// the temp public key travels with the ciphertext (required to decrypt).
    /// </summary>
    public static EccEncryptedPayload Seal(EccPublicKeyData storePublicKey, byte[] payload)
    {
        var pwd = ByteArrayUtil.GetRndByteArray(16).ToSensitiveByteArray();
        var tempKey = new EccFullKeyData(pwd, EccKeySize.P384, hours: 2);

        var salt = ByteArrayUtil.GetRndByteArray(16);
        var sharedSecret = tempKey.GetEcdhSharedSecret(pwd, storePublicKey, salt);
        var iv = ByteArrayUtil.GetRndByteArray(16);

        return new EccEncryptedPayload
        {
            RemotePublicKeyJwk = tempKey.PublicKeyJwk(),
            Iv = iv,
            EncryptedData = AesGcm.Encrypt(payload, sharedSecret, iv),
            Salt = salt,
            EncryptionPublicKeyCrc32 = storePublicKey.crc32c,
            KeyType = PublicPrivateKeyType.PeerKeyStoreWriteOnlyKey
        };
    }

    /// <summary>Recovers a sealed payload: key store key → store private key → ECDH with the
    /// sealer's temp public key → decrypt.</summary>
    public static byte[] Unseal(EccFullKeyData storeKeyPair, SensitiveByteArray keyStoreKey, EccEncryptedPayload payload)
    {
        var sealerPublicKey = EccPublicKeyData.FromJwkPublicKey(payload.RemotePublicKeyJwk);
        var sharedSecret = storeKeyPair.GetEcdhSharedSecret(keyStoreKey, sealerPublicKey, payload.Salt);
        return AesGcm.Decrypt(payload.EncryptedData, sharedSecret, payload.Iv);
    }
}

/// <summary>
/// Storage-key source backed by a <see cref="DepositedGrant"/>'s sealed keys: used when
/// converting a deposit into a normal circle grant, so conversion is just a re-mint.
/// </summary>
public sealed class DepositedGrantStorageKeySource(
    DepositedGrant deposit,
    EccFullKeyData storeKeyPair,
    SensitiveByteArray keyStoreKey) : IStorageKeySource
{
    public SensitiveByteArray? GetStorageKey(StorageDrive drive)
    {
        var sealedKey = deposit.DriveGrants.FirstOrDefault(dg => dg.DriveId == drive.Id)?.SealedStorageKey;
        if (sealedKey == null)
        {
            // Permission-only entry, or the drive joined the circle after the deposit;
            // the grant is minted keyless and the owner's next touch re-mints it.
            return null;
        }

        return PeerKeyStoreWriteOnlyKey.Unseal(storeKeyPair, keyStoreKey, sealedKey).ToSensitiveByteArray();
    }
}
