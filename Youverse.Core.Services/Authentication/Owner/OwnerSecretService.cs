using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Youverse.Core.Cryptography;
using Youverse.Core.Cryptography.Crypto;
using Youverse.Core.Cryptography.Data;
using Youverse.Core.Exceptions;
using Youverse.Core.Services.Base;

namespace Youverse.Core.Services.Authentication.Owner
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
            var key = RsaKeyListManagement.GetCurrentKey(ref RsaKeyListManagement.zeroSensitiveKey, ref rsaKeyList, out var keyListWasUpdated); // TODO
            if (keyListWasUpdated)
            {
                _systemStorage.WithTenantSystemStorage<RsaFullKeyListData>(RSA_KEY_STORAGE, s => s.Save(rsaKeyList));
            }

            var nonce = NonceData.NewRandomNonce(key);
            _systemStorage.WithTenantSystemStorage<NonceData>(STORAGE, s => s.Save(nonce));
            return nonce;
        }


        public async Task SetNewPassword(PasswordReply reply)
        {
            
            var existingPwd = await _systemStorage.WithTenantSystemStorageReturnSingle<PasswordData>(PWD_STORAGE, s => s.Get(PasswordData.Key));
            if (null != existingPwd)
            {
                throw new YouverseSecurityException("Password already set");
            }
            
            
            Guid originalNoncePackageKey = new Guid(Convert.FromBase64String(reply.Nonce64));
            var originalNoncePackage = await _systemStorage.WithTenantSystemStorageReturnSingle<NonceData>(STORAGE, s => s.Get(originalNoncePackageKey));

            //HACK: this will be moved to the overall provisioning process
            //await this.GenerateRsaKeyList();
            var keys = await this.GetRsaKeyList();
            
            var pk = PasswordDataManager.SetInitialPassword(originalNoncePackage, reply, keys);
            _systemStorage.WithTenantSystemStorage<PasswordData>(PWD_STORAGE, s => s.Save(pk));

            //delete the temporary salts
            _systemStorage.WithTenantSystemStorage<NonceData>(STORAGE, s => s.Delete(originalNoncePackageKey));
        }

        public async Task<SensitiveByteArray> GetMasterKey(OwnerConsoleToken serverToken, SensitiveByteArray clientSecret)
        {
            var pk = await _systemStorage.WithTenantSystemStorageReturnSingle<PasswordData>(PWD_STORAGE, s => s.Get(PasswordData.Key));
            if (null == pk)
            {
                throw new InvalidDataException("Secrets configuration invalid.  Did you initialize a password?");
            }

            //TODO: do we want to keep the extra layer of having a client and
            //server halfs to form the kek. then use that kek to decrypt the master key?
            var kek = serverToken.TokenEncryptedKek.DecryptKey(ref clientSecret);

            var masterKey = pk.KekEncryptedMasterKey.DecryptKey(ref kek);

            // masterKey.Wipe(); <- removed. The EncryptedDek class will zap this key on its destruction.
            serverToken.Dispose();

            return masterKey;
        }

        public async Task<SaltsPackage> GetStoredSalts()
        {
            var pk = await _systemStorage.WithTenantSystemStorageReturnSingle<PasswordData>(PWD_STORAGE, s => s.Get(PasswordData.Key));

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

        public async Task<RsaFullKeyListData> GenerateRsaKeyList()
        {
            const int MAX_KEYS = 2; //leave this size 

            var rsaKeyList = RsaKeyListManagement.CreateRsaKeyList(ref RsaKeyListManagement.zeroSensitiveKey, MAX_KEYS); // TODO
            rsaKeyList.Id = RSA_KEY_STORAGE_ID;

            _systemStorage.WithTenantSystemStorage<RsaFullKeyListData>(RSA_KEY_STORAGE, s => s.Save(rsaKeyList));
            return rsaKeyList;
        }

        public async Task<RsaFullKeyListData> GetRsaKeyList()
        {
            var result = await _systemStorage.WithTenantSystemStorageReturnSingle<RsaFullKeyListData>(RSA_KEY_STORAGE, s => s.Get(RSA_KEY_STORAGE_ID));

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
            var pk = await _systemStorage.WithTenantSystemStorageReturnSingle<PasswordData>(PWD_STORAGE, s => s.Get(PasswordData.Key));

            // TODO XXX Where the heck do we validate the server has the nonce64 (prevent replay)

            PasswordDataManager.TryPasswordKeyMatch(pk, nonceHashedPassword64, nonce64);
        }
    }
}