namespace Odin.Core.Services.EncryptionKeyService
{
    /// <summary>
    /// A set of data encrypted using an Rsa Public Key
    /// </summary>
    public class RsaEncryptedPayload
    {
        /// <summary>
        /// The Rsa Encrypted key header whose value is a <see cref="RsaEncryptedKeyHeader"/> used to encrypt the <see cref="Data"/>
        /// </summary>
        public byte[] RsaEncryptedKeyHeader { get; set; }

        /// <summary>
        /// The encrypted payload
        /// </summary>
        public byte[] Data { get; set; }

        /// <summary>
        /// The CRC of the public key used to encrypt this paylaod
        /// </summary>
        public uint Crc32 { get; set; }
    }
}