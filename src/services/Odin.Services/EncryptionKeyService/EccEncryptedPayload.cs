using System;
using Odin.Core;

namespace Odin.Services.EncryptionKeyService;

public class EccEncryptedPayload
{
    // PROBABLY MERGE IV & SALT

    /// <summary>
    /// Initialization Vector for EncryptedData
    /// </summary>
    public byte[] Iv { get; set; }

    /// <summary>
    /// The encrypted data
    /// </summary>
    public byte[] EncryptedData { get; set; }

    /// <summary>
    /// Remote public key used to generate the shared secret for encrypting the EncryptedData
    /// </summary>
    public string RemotePublicKeyJwk { get; set; }

    /// <summary>
    /// Salt used for generating the shared secret for encrypted the EncryptedData
    /// </summary>
    public byte[] Salt { get; set; }

    /// <summary>
    /// The crc of the host's public key used to encrypt this payload
    /// </summary>
    public uint EncryptionPublicKeyCrc32 { get; set; }

    public PublicPrivateKeyType KeyType { get; set; } = PublicPrivateKeyType.OnlineIcrEncryptedKey; // defaulting to OnlineIcrEncryptedKey because backwards-compatibility 

    /// <summary>
    /// Specifies the time this was encrypted in the form of a SequentialGuid
    /// </summary>
    public Guid TimestampId { get; set; } = SequentialGuid.CreateGuid();
}