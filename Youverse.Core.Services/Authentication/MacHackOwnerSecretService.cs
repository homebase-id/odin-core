using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Youverse.Core.Cryptography;
using Youverse.Core.Cryptography.Data;
using Youverse.Core.Services.Base;

namespace Youverse.Core.Services.Authentication
{
    public class MacHackOwnerSecretService : OwnerSecretService
    {
        public MacHackOwnerSecretService(DotYouContext context, ILogger logger) : base(context, logger)
        {
        }

        /// <summary>
        /// Generates two 16 byte crypto-random numbers used for salting passwords
        /// </summary>
        /// <returns></returns>
        public override async Task<NonceData> GenerateNewSalts()
        {
            var key = new RsaKeyData()
            {
                crc32c = 0,
                instantiated = DateTimeExtensions.UnixTimeSeconds(),
                expiration = DateTimeExtensions.UnixTimeSeconds() + (UInt64) 10 * 3600 + (UInt64) 5 * 60 + (UInt64) 5,
                iv = Guid.Empty,
                privateKey = Guid.Empty.ToByteArray(),
                publicKey = Guid.Empty.ToByteArray(),
                encrypted = false
            };

            var nonce = NonceData.NewRandomNonce(key);
            WithTenantSystemStorage<NonceData>(STORAGE, s => s.Save(nonce));
            return nonce;
        }

        public override async Task SetNewPassword(PasswordReply reply)
        {
            Guid originalNoncePackageKey = new Guid(Convert.FromBase64String(reply.Nonce64));
            var originalNoncePackage = await WithTenantSystemStorageReturnSingle<NonceData>(STORAGE, s => s.Get(originalNoncePackageKey));

            var pk = new LoginKeyData()
            {
                SaltPassword = Guid.Empty.ToByteArray(),
                SaltKek = Guid.Empty.ToByteArray(),
                HashPassword = Guid.Empty.ToByteArray(),
                XorEncryptedDek = Guid.Empty.ToByteArray()
            };
            WithTenantSystemStorage<LoginKeyData>(PWD_STORAGE, s => s.Save(pk));

            //delete the temporary salts
            WithTenantSystemStorage<NonceData>(STORAGE, s => s.Delete(originalNoncePackageKey));
        }
    }
}