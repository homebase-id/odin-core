using System;
using Youverse.Core.Cryptography.Crypto;

namespace Youverse.Core.Cryptography.Data
{
    /// <summary>
    /// Holds salts used during a delicate process wherein you need to hash
    /// and salt passwords yet hold a copy of the Nonce serverside to ensure
    /// </summary>
    public sealed class NonceData
    {
        public static NonceData NewRandomNonce(RsaKeyData keyData)
        {
            var np = new NonceData()
            {
                Nonce64 = Convert.ToBase64String(ByteArrayUtil.GetRndByteArray(CryptographyConstants.SALT_SIZE)),
                SaltPassword64 = Convert.ToBase64String(ByteArrayUtil.GetRndByteArray(CryptographyConstants.SALT_SIZE)),
                SaltKek64 = Convert.ToBase64String(ByteArrayUtil.GetRndByteArray(CryptographyConstants.SALT_SIZE)),
                PublicPem  = RsaKeyManagement.publicPem(keyData),
                CRC = keyData.crc32c
            };

            if (np.SaltPassword64 == np.SaltKek64)
                throw new Exception("Impossibly unlikely");

            return np;
        }

        public NonceData()
        {
        }

        /// <summary>
        /// Creates a new NoncePackage using specified salts and generates a random <see cref="Nonce64"/> value
        /// </summary>
        /// <param name="saltPassword64"></param>
        /// <param name="saltKek64"></param>
        public NonceData(string saltPassword64, string saltKek64, string pem, UInt32 crc)
        {
             // Guard.Argument(saltPassword, nameof(saltPassword)).NotEmpty().Require(x => x.Length == IdentityKeySecurity.SALT_SIZE);
             // Guard.Argument(saltKek, nameof(saltKek)).NotEmpty().Require(x => x.Length == IdentityKeySecurity.SALT_SIZE);

            Nonce64 = Convert.ToBase64String(ByteArrayUtil.GetRndByteArray(CryptographyConstants.SALT_SIZE));
            SaltPassword64 = saltPassword64;
            SaltKek64 = saltKek64;
            PublicPem = pem;
            CRC = crc;
        }

        public Guid Id
        {
            get
            {
                return new Guid(Convert.FromBase64String(this.Nonce64));
            }
            set
            {
                
            }
        }
        public string SaltPassword64 { get; set; }
        public string SaltKek64 { get; set; }
        public string Nonce64 { get; set; }
        public string PublicPem { get; set; }
        public UInt32 CRC { get; set; }
    }
}