using System;

namespace Odin.Core.Cryptography.Data
{
    /// <summary>
    /// Holds salts used during a delicate process wherein you need to hash
    /// and salt passwords yet hold a copy of the Nonce serverside to ensure
    /// </summary>
    public sealed class NonceData
    {
        public static NonceData NewRandomNonce(EccPublicKeyData keyData, int hashSize)
        {
            var np = new NonceData()
            {
                Nonce64 = Convert.ToBase64String(ByteArrayUtil.GetRndByteArray(hashSize)),
                SaltPassword64 = Convert.ToBase64String(ByteArrayUtil.GetRndByteArray(hashSize)),
                SaltKek64 = Convert.ToBase64String(ByteArrayUtil.GetRndByteArray(hashSize)),
                PublicJwk = keyData.PublicKeyJwk(),
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
        public NonceData(string saltPassword64, string saltKek64, string jwk, UInt32 crc, int hashSize)
        {
            // Guard.Argument(saltPassword, nameof(saltPassword)).NotEmpty().Require(x => x.Length == IdentityKeySecurity.SALT_SIZE);
            // Guard.Argument(saltKek, nameof(saltKek)).NotEmpty().Require(x => x.Length == IdentityKeySecurity.SALT_SIZE);

            Nonce64 = Convert.ToBase64String(ByteArrayUtil.GetRndByteArray(hashSize));
            SaltPassword64 = saltPassword64;
            SaltKek64 = saltKek64;
            PublicJwk = jwk;
            CRC = crc;
        }

        public Guid Id => string.IsNullOrEmpty(Nonce64) ? Guid.Empty : new Guid(Convert.FromBase64String(Nonce64));
        public string SaltPassword64 { get; set; }
        public string SaltKek64 { get; set; }
        public string Nonce64 { get; set; }
        public string PublicJwk { get; set; }
        public UInt32 CRC { get; set; }
    }
}