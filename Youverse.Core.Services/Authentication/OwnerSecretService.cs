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
    public class OwnerSecretService :  IOwnerSecretService
    {
        protected const string STORAGE = "Provisioning";
        protected const string PWD_STORAGE = "k3";
        protected const string RSA_KEY_STORAGE = "rks";
        protected readonly Guid RSA_KEY_STORAGE_ID = Guid.Parse("FFFFFFCF-0f85-DDDD-a7eb-e8e0b06c2555");

        protected readonly ISystemStorage _systemStorage;

        public OwnerSecretService(DotYouContext context, ILogger<IOwnerSecretService> logger, ISystemStorage systemStorage)
        {
            _systemStorage = systemStorage;
        }

        /// <summary>
        /// Generates two 16 byte crypto-random numbers used for salting passwords
        /// </summary>
        /// <returns></returns>
        public async Task<NonceData> GenerateNewSalts()
        {
            var rsaKeyList = await this.GetRsaKeyList();
            var key = RsaKeyListManagement.GetCurrentKey(ref rsaKeyList, out var keyListWasUpdated);
            if (keyListWasUpdated)
            {
                _systemStorage.WithTenantSystemStorage<RsaKeyListData>(RSA_KEY_STORAGE, s => s.Save(rsaKeyList));
            }

            var nonce = NonceData.NewRandomNonce(key);
            _systemStorage.WithTenantSystemStorage<NonceData>(STORAGE, s => s.Save(nonce));
            return nonce;
        }


        public async Task SetNewPassword(PasswordReply reply)
        {
            Guid originalNoncePackageKey = new Guid(Convert.FromBase64String(reply.Nonce64));
            var originalNoncePackage = await _systemStorage.WithTenantSystemStorageReturnSingle<NonceData>(STORAGE, s => s.Get(originalNoncePackageKey));

            //HACK: this will be moved to the overall provisioning process
            //await this.GenerateRsaKeyList();
            var keys = await this.GetRsaKeyList();

            var pk = LoginKeyManager.SetInitialPassword(originalNoncePackage, reply, keys);
            _systemStorage.WithTenantSystemStorage<LoginKeyData>(PWD_STORAGE, s => s.Save(pk));

            //delete the temporary salts
            _systemStorage.WithTenantSystemStorage<NonceData>(STORAGE, s => s.Delete(originalNoncePackageKey));
        }

        public async Task<SecureKey> GetDek(LoginTokenData loginToken, SecureKey clientHalfKek)
        {
            var pk = await _systemStorage.WithTenantSystemStorageReturnSingle<LoginKeyData>(PWD_STORAGE, s => s.Get(LoginKeyData.Key));
            if (null == pk)
            {
                throw new InvalidDataException("Secrets configuration invalid.  Did you initialize a password?");
            }

            var loginKek = LoginTokenManager.GetLoginKek(loginToken.HalfKey, clientHalfKek.GetKey());

            var dek = pk.EncryptedDek.DecryptKey(loginKek.GetKey());

            loginKek.Wipe();
            loginToken.Dispose();

            return dek;
        }

        public async Task<SaltsPackage> GetStoredSalts()
        {
            var pk = await _systemStorage.WithTenantSystemStorageReturnSingle<LoginKeyData>(PWD_STORAGE, s => s.Get(LoginKeyData.Key));

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

        public async Task<RsaKeyListData> GenerateRsaKeyList()
        {
            const int MAX_KEYS = 2; //leave this size 

            var rsaKeyList = RsaKeyListManagement.CreateRsaKeyList(MAX_KEYS);
            rsaKeyList.Id = RSA_KEY_STORAGE_ID;

            _systemStorage.WithTenantSystemStorage<RsaKeyListData>(RSA_KEY_STORAGE, s => s.Save(rsaKeyList));
            return rsaKeyList;
        }

        public async Task<RsaKeyListData> GetRsaKeyList()
        {
            var result = await _systemStorage.WithTenantSystemStorageReturnSingle<RsaKeyListData>(RSA_KEY_STORAGE, s => s.Get(RSA_KEY_STORAGE_ID));

            if (result == null || result.ListRSA == null)
            {
                return await this.GenerateRsaKeyList();
            }

            return result;
        }

        // Given the client's nonce and nonceHash, load the identitys passwordKey info
        // and with that info we can validate if the client calculated the right hash.
        public async Task TryPasswordKeyMatch(string nonceHashedPassword64, string nonce64)
        {
            var pk = await _systemStorage.WithTenantSystemStorageReturnSingle<LoginKeyData>(PWD_STORAGE, s => s.Get(LoginKeyData.Key));

            // TODO XXX Where the heck do we validate the server has the nonce64 (prevent replay)

            LoginKeyManager.TryPasswordKeyMatch(pk, nonceHashedPassword64, nonce64);
        }
    }
}