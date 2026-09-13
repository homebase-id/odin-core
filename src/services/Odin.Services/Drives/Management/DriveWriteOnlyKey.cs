using Odin.Core;
using Odin.Core.Cryptography.Data;
using Odin.Core.Serialization;

namespace Odin.Services.Drives.Management;

/// <summary>
/// The write-without-read primitive for a drive: an ECC-384 keypair whose private half is escrowed
/// under the drive's own storage key.  Anyone holding the public half can seal a deposit TO the
/// drive; only a holder of the storage key can unseal it (docs/drive-addressing.md).
/// </summary>
/// <remarks>
/// Deliberately the same shape as the shipped <c>PeerKeyStoreWriteOnlyKey</c>, which does this for a
/// connection's key store.  The only difference is which symmetric key escrows the private half --
/// there the key store key, here the drive's storage key -- which is what makes deposit-collection
/// custody equal to existing read access rather than a new thing to grant.
/// </remarks>
public static class DriveWriteOnlyKey
{
    // Effectively non-expiring; EccFullKeyData requires a lifespan. Same value the connection-level
    // keypair uses, and for the same reason: the key's lifetime is the drive's.
    private const int LifetimeHours = 24 * 365 * 50;

    /// <summary>Mints a drive's keypair, escrowing the private half under its storage key.</summary>
    public static EccFullKeyData CreateKeyPair(SensitiveByteArray storageKey)
    {
        return new EccFullKeyData(storageKey, EccKeySize.P384, LifetimeHours);
    }

    /// <summary>
    /// Serializes for the <c>Drives.WriteOnlyKeyPair</c> column.  Null in, null out: a drive with no
    /// keypair stores NULL, which is what says deposits are not enabled for it.
    /// </summary>
    public static byte[] Serialize(EccFullKeyData keyPair)
    {
        return keyPair == null ? null : OdinSystemSerializer.Serialize(keyPair).ToUtf8ByteArray();
    }

    /// <summary>Reads the column back; null and empty both mean "no keypair".</summary>
    public static EccFullKeyData Deserialize(byte[] stored)
    {
        if (stored == null || stored.Length == 0)
        {
            return null;
        }

        return OdinSystemSerializer.Deserialize<EccFullKeyData>(stored.ToStringFromUtf8Bytes());
    }
}
