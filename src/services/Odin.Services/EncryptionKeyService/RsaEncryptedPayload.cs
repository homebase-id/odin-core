namespace Odin.Services.EncryptionKeyService
{
    /// <summary>
    /// A set of data encrypted using an Rsa Public Key
    /// </summary>
    public class RsaEncryptedPayload
    {
        /// <summary>
        /// The Rsa Encrypted key header whose value is a <see cref="RsaEncryptedKeyHeader"/> used to encrypt the <see cref="KeyHeaderEncryptedData"/>
        /// </summary>
        public byte[] RsaEncryptedKeyHeader { get; set; }

        /// <summary>
        /// The encrypted payload
        /// </summary>
        public byte[] KeyHeaderEncryptedData { get; set; }

        /// <summary>
        /// The CRC of the public key used to encrypt this paylaod
        /// </summary>
        public uint Crc32 { get; set; }

        public bool IsValid()
        {
            var isBad = this.KeyHeaderEncryptedData == null || this.RsaEncryptedKeyHeader == null || this.Crc32 == 0;
            return !isBad;
        }
    }
    
    public class EccEncryptedPayload
    {
        /// <summary>
        /// Initialization Vector for EncryptedData
        /// </summary>
        public byte[] Iv { get; set; }

        /// <summary>
        /// The encrypted data
        /// </summary>
        public byte[] EncryptedData { get; set; }

        /// <summary>
        /// Public key used to generate the shared secret for encrypting the EncryptedData
        /// </summary>
        public string PublicKey { get; set; }

        /// <summary>
        /// Salt used for generating the shared secret for encrypted the EncryptedData
        /// </summary>
        public byte[] Salt { get; set; }

        /// <summary>
        /// The crc of the public key used to encrypt this payload
        /// </summary>
        public uint RecipientPublicKeyCrc32 { get; set; }
    }
}