using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Youverse.Core.Cryptography;
using Youverse.Core.Cryptography.Crypto;
using Youverse.Core.Cryptography.Data;
using Youverse.Core.Services.Base;

namespace Youverse.Core.Services.Authentication
{
    public class OwnerSecretService : DotYouServiceBase, IOwnerSecretService
    {
        private const string STORAGE = "Provisioning";
        private const string PWD_STORAGE = "k3";
        private const string RSA_KEY_STORAGE = "rks";
        private readonly Guid RSA_KEY_STORAGE_ID = Guid.Parse("FFFFFFCF-0f85-DDDD-a7eb-e8e0b06c2555");

        public OwnerSecretService(DotYouContext context, ILogger logger) : base(context, logger, null, null)
        {
        }

        /// <summary>
        /// Generates two 16 byte crypto-random numbers used for salting passwords
        /// </summary>
        /// <returns></returns>
        public async Task<NonceData> GenerateNewSalts()
        {
            //HACK: this will be moved to the overall provisioning process
            await this.GenerateRsaKeyList();
            var rsa = await this.GetRsaKeyList();

            var key = RsaKeyListManagement.GetCurrentKey(rsa);
            
            var nonce = NonceData.NewRandomNonce(key);
            WithTenantSystemStorage<NonceData>(STORAGE, s => s.Save(nonce));
            return nonce;
        }


        public async Task SetNewPassword(PasswordReply reply)
        {
            Guid originalNoncePackageKey = new Guid(Convert.FromBase64String(reply.Nonce64));
            var originalNoncePackage = await WithTenantSystemStorageReturnSingle<NonceData>(STORAGE, s => s.Get(originalNoncePackageKey));

            //HACK: this will be moved to the overall provisioning process
            //await this.GenerateRsaKeyList();
            var keys = await this.GetRsaKeyList();

            var pk = LoginKeyManager.SetInitialPassword(originalNoncePackage, reply, keys);
            WithTenantSystemStorage<LoginKeyData>(PWD_STORAGE, s => s.Save(pk));

            //delete the temporary salts
            WithTenantSystemStorage<NonceData>(STORAGE, s => s.Delete(originalNoncePackageKey));
        }

        public async Task<SecureKey> GetEncryptedDek()
        {
            var pk = await WithTenantSystemStorageReturnSingle<LoginKeyData>(PWD_STORAGE, s => s.Get(LoginKeyData.Key));

            if (null == pk)
            {
                throw new InvalidDataException("Secrets configuration invalid.  Did you initialize a password?");
            }

            return new SecureKey(pk.XorEncryptedDek);
        }
        
        public async Task<SaltsPackage> GetStoredSalts()
        {
            var pk = await WithTenantSystemStorageReturnSingle<LoginKeyData>(PWD_STORAGE, s => s.Get(LoginKeyData.Key));

            if (null == pk)
            {
                throw new InvalidDataException("Secrets configuration invalid.  Did you initialize a password?");
            }

            return new SaltsPackage()
            {
                SaltKek64 = Convert.ToBase64String(pk.SaltKek),
                SaltPassword64 = Convert.ToBase64String(pk.SaltPassword)
            };
        }

        public Task GenerateRsaKeyList()
        {
            //HACK: need to refactor this when storage is rebuilt 
            const int MAX_KEYS = 2; //leave this size 

            var rsaKeyList = RsaKeyListManagement.CreateRsaKeyList(MAX_KEYS);
            rsaKeyList.Id = RSA_KEY_STORAGE_ID;
            var keys = rsaKeyList.ListRSA.ToArray();
                
            //HACK : mapping to ensure storage works 
            var storage = new RsaKeyStorage()
            {
                Id = rsaKeyList.Id,
                Keys = new List<RsaKeyData>(keys)
            };

            WithTenantSystemStorage<RsaKeyStorage>(RSA_KEY_STORAGE, s => s.Save(storage));
            return Task.CompletedTask;
        }

        public async Task<RsaKeyListData> GetRsaKeyList()
        {
            var result = await WithTenantSystemStorageReturnSingle<RsaKeyStorage>(RSA_KEY_STORAGE, s => s.Get(RSA_KEY_STORAGE_ID));
            
            //HACK CONVERT from storage
            RsaKeyListData converted = new RsaKeyListData()
            {
                Id = result.Id,
                MaxKeys = result.Keys.Count,
                ListRSA = new List<RsaKeyData>(result.Keys)
            };

            return converted;
        }

        // Given the client's nonce and nonceHash, load the identitys passwordKey info
        // and with that info we can validate if the client calculated the right hash.
        public async Task TryPasswordKeyMatch(string nonceHashedPassword64, string nonce64)
        {
            var pk = await WithTenantSystemStorageReturnSingle<LoginKeyData>(PWD_STORAGE, s => s.Get(LoginKeyData.Key));

            // TODO XXX Where the heck do we validate the server has the nonce64 (prevent replay)

            LoginKeyManager.TryPasswordKeyMatch(pk, nonceHashedPassword64, nonce64);
        }
    }
}