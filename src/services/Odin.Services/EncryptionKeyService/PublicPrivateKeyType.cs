namespace Odin.Services.EncryptionKeyService;

public enum PublicPrivateKeyType
{
    OfflineKey,
    OnlineKey,

    /// <summary>
    /// An online key where the private key is encrypted using the ICR key
    /// </summary>
    OnlineIcrEncryptedKey,

    /// <summary>
    /// A connection-scoped write-only key: the private key is escrowed under that
    /// connection's key store key (the Peer Key). Not an identity-level key; payloads with
    /// this type are addressed to a specific PeerKeyStore and are never routed through
    /// PublicPrivateKeyService.
    /// </summary>
    PeerKeyStoreWriteOnlyKey
}