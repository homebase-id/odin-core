using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Youverse.Core.Cryptography;
using Youverse.Core.Cryptography.Crypto;
using Youverse.Core.Cryptography.Data;
using Youverse.Core.Services.Base;

namespace Youverse.Core.Services.Authentication
{
    public class MacHackOwnerSecretService : OwnerSecretService
    {
        public MacHackOwnerSecretService(DotYouContext context, ILogger<MacHackOwnerSecretService> logger, ISystemStorage systemStorage) : base(context, logger, systemStorage)
        {
        }

        /// <summary>
        /// Generates two 16 byte crypto-random numbers used for salting passwords
        /// </summary>
        /// <returns></returns>
        public override async Task<NonceData> GenerateNewSalts()
        {
            var publicKey64 = "MIIBIjANBgkqhkiG9w0BAQEFAAOCAQ8AMIIBCgKCAQEA6Tt75Wgd7iVOlFk9sTl/+d/oiiMPNH5NtHaK6uOPE1GRCSXWbvvY46+vrgNIk3DZCDSPCk26e0U+AvB/mwtZFaqcRrg3rbO2jcGQWybYZdTA+UqQNVi1BSxRCRlFptGoM+pdGnnAG8o80VwWZlryUPiMXM2FF/BhHSOxDoMfXgFKJnxc+4Mvdzu5qYA+/ivjgCmT+zUhb00eSWnCCgnB4SXRFP/VZB2isH/ovfJ6kTGDE+e1Ct3gQD6mst0CcSe9YvXhYhADqjOO5nLIq4b+BXoM18ce4qy9t75/AmdW9PdOx7CikVDHNrhVwYAt9rNTnftW9yAPmUX9pGydoAlyqQIDAQAB";
            var publicKey = Convert.FromBase64String(publicKey64);
            var privateKey = Guid.Parse("0000000F-0f85-DDDD-a7eb-e8e0b06c2555").ToByteArray();

            var key = new RsaKeyData()
            {
                crc32c = CRC32C.CalculateCRC32C(0, publicKey),
                instantiated = DateTimeExtensions.UnixTimeSeconds(),
                expiration = DateTimeExtensions.UnixTimeSeconds() + (UInt64) 10 * 3600 + (UInt64) 5 * 60 + (UInt64) 5,
                iv = Guid.Empty,
                privateKey = privateKey,
                publicKey = publicKey,
                encrypted = false
            };

            var nonce = NonceData.NewRandomNonce(key);
            _systemStorage.WithTenantSystemStorage<NonceData>(STORAGE, s => s.Save(nonce));
            return nonce;
        }

        public override async Task SetNewPassword(PasswordReply reply)
        {
            Guid originalNoncePackageKey = new Guid(Convert.FromBase64String(reply.Nonce64));
            var originalNoncePackage = await _systemStorage.WithTenantSystemStorageReturnSingle<NonceData>(STORAGE, s => s.Get(originalNoncePackageKey));

            var pk = new LoginKeyData()
            {
                SaltPassword = Guid.Empty.ToByteArray(),
                SaltKek = Guid.Empty.ToByteArray(),
                HashPassword = Guid.Empty.ToByteArray(),
                XorEncryptedDek = Guid.Empty.ToByteArray()
            };
            _systemStorage.WithTenantSystemStorage<LoginKeyData>(PWD_STORAGE, s => s.Save(pk));

            //delete the temporary salts
            _systemStorage.WithTenantSystemStorage<NonceData>(STORAGE, s => s.Delete(originalNoncePackageKey));
        }
    }
}