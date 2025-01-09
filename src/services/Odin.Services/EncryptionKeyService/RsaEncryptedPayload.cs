using Odin.Core.Identity;
using Odin.Core.Time;

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
}