namespace Odin.Services.EncryptionKeyService;

public enum PublicPrivateKeyType
{
    OfflineKey,
    OnlineKey,
    // An online key where the private key is encrypted using the ICR key
    OnlineIcrEncryptedKey
}