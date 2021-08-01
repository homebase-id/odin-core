using System;
using System.IO;
using System.Threading.Tasks;
using DotYou.IdentityRegistry;
using DotYou.Kernel.Cryptography;
using DotYou.Types;
using DotYou.Types.Cryptography;
using Microsoft.AspNetCore.Cryptography.KeyDerivation;
using Microsoft.Extensions.Logging;

namespace DotYou.Kernel.Services.Admin.Authentication
{
    public class OwnerSecretService : DotYouServiceBase, IOwnerSecretService
    {
        private const string STORAGE = "Provisioning";
        private const string PWD_STORAGE = "k3";

        public OwnerSecretService(DotYouContext context, ILogger logger) : base(context, logger, null, null)
        {
        }

        /// <summary>
        /// Generates two 16 byte crypto-random numbers used for salting passwords
        /// </summary>
        /// <returns></returns>
        public Task<NoncePackage> GenerateNewSalts()
        {
            var nonce = NoncePackage.NewRandomNonce("xxx");
            WithTenantStorage<NoncePackage>(STORAGE, s => s.Save(nonce));
            return Task.FromResult(nonce);
        }

        public async Task SetNewPassword(PasswordReply reply)
        {
            Guid originalNoncePackageKey = new Guid(Convert.FromBase64String(reply.Nonce64));
            var originalNoncePackage = await WithTenantStorageReturnSingle<NoncePackage>(STORAGE, s => s.Get(originalNoncePackageKey));

            var pk = LoginKeyManager.SetInitialPassword(originalNoncePackage, reply, null); // XXX
            WithTenantStorage<LoginKeyData>(PWD_STORAGE, s => s.Save(pk));

            //delete the temporary salts
            WithTenantStorage<NoncePackage>(STORAGE, s => s.Delete(originalNoncePackageKey));
        }


        public async Task<SaltsPackage> GetStoredSalts()
        {
            var pk = await WithTenantStorageReturnSingle<LoginKeyData>(PWD_STORAGE, s => s.Get(LoginKeyData.Key));

            if (null == pk)
            {
                throw new InvalidDataException("Secrets configuration invalid");
            }

            return new SaltsPackage()
            {
                SaltKek64 = Convert.ToBase64String(pk.SaltKek),
                SaltPassword64 = Convert.ToBase64String(pk.SaltPassword)
            };
        }

        public async Task TryPasswordKeyMatch(string nonceHashedPassword64, string nonce64)
        {
            var pk = await WithTenantStorageReturnSingle<LoginKeyData>(PWD_STORAGE, s => s.Get(LoginKeyData.Key));

            LoginKeyManager.TryPasswordKeyMatch(pk, nonceHashedPassword64, nonce64);
        }
    }
}
