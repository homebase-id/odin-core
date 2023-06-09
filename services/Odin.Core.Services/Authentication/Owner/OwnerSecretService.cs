using System;
using System.Threading.Tasks;
using Odin.Core.Cryptography;
using Odin.Core.Cryptography.Crypto;
using Odin.Core.Cryptography.Data;
using Odin.Core.Exceptions;
using Odin.Core.Services.Base;

namespace Odin.Core.Services.Authentication.Owner
{
    public class OwnerSecretService : IOwnerSecretService
    {
        private readonly GuidId _passwordKey = GuidId.FromString("_passwordKey");
        private readonly GuidId _rsaKeyStorageId = GuidId.FromString("_rsaKeyStorageId");

        private readonly TenantSystemStorage _tenantSystemStorage;
        private readonly TenantContext _tenantContext;

        public OwnerSecretService(TenantContext tenantContext, TenantSystemStorage tenantSystemStorage)
        {
            _tenantContext = tenantContext;
            _tenantSystemStorage = tenantSystemStorage;
        }

        /// <summary>
        /// Generates two 16 byte crypto-random numbers used for salting passwords
        /// </summary>
        /// <returns></returns>
        public async Task<NonceData> GenerateNewSalts()
        {
            var rsaKeyList = await this.GetOfflineRsaKeyList();
            var key = RsaKeyListManagement.GetCurrentKey(rsaKeyList);
            var nonce = NonceData.NewRandomNonce(key);
            _tenantSystemStorage.SingleKeyValueStorage.Upsert(nonce.Id, nonce);

            return nonce;
        }

        public async Task SetNewPassword(PasswordReply reply)
        {
            bool canSet = reply.FirstRunToken == _tenantContext.FirstRunToken || _tenantContext.IsPreconfigured;
            if (!canSet)
            {
                throw new OdinSystemException("Invalid first run token; cannot set password");
            }
            if (await IsMasterPasswordSet())
            {
                throw new OdinSecurityException("Password already set");
            }

            Guid originalNoncePackageKey = new Guid(Convert.FromBase64String(reply.Nonce64));
            var originalNoncePackage = _tenantSystemStorage.SingleKeyValueStorage.Get<NonceData>(originalNoncePackageKey);

            var keys = await this.GetOfflineRsaKeyList();

            var pk = PasswordDataManager.SetInitialPassword(originalNoncePackage, reply, keys);
            _tenantSystemStorage.SingleKeyValueStorage.Upsert(_passwordKey, pk);

            //delete the temporary salts
            _tenantSystemStorage.SingleKeyValueStorage.Delete(originalNoncePackageKey);
        }

        public Task<bool> IsMasterPasswordSet()
        {
            var existingPwd = _tenantSystemStorage.SingleKeyValueStorage.Get<PasswordData>(_passwordKey);
            return Task.FromResult(existingPwd != null);
        }

        public async Task<SensitiveByteArray> GetMasterKey(OwnerConsoleToken serverToken, SensitiveByteArray clientSecret)
        {
            var pk = _tenantSystemStorage.SingleKeyValueStorage.Get<PasswordData>(_passwordKey);
            if (null == pk)
            {
                throw new OdinClientException("Secrets configuration invalid. Did you initialize a password?");
            }

            //TODO: do we want to keep the extra layer of having a client and
            //server halfs to form the kek. then use that kek to decrypt the master key?
            var kek = serverToken.TokenEncryptedKek.DecryptKeyClone(ref clientSecret);

            var masterKey = pk.KekEncryptedMasterKey.DecryptKeyClone(ref kek);

            // masterKey.Wipe(); <- removed. The EncryptedDek class will zap this key on its destruction.
            serverToken.Dispose();

            return await Task.FromResult(masterKey);
        }

        public async Task<(uint publicKeyCrc32C, string publicKeyPem)> GetCurrentAuthenticationRsaKey()
        {
            var rsaKeyList = await this.GetOfflineRsaKeyList();
            var key = RsaKeyListManagement.GetCurrentKey(rsaKeyList);
            return (key.crc32c, key.publicPem());
        }

        public async Task<SaltsPackage> GetStoredSalts()
        {
            var pk = _tenantSystemStorage.SingleKeyValueStorage.Get<PasswordData>(_passwordKey);

            if (null == pk)
            {
                throw new OdinClientException("Secrets configuration invalid. Did you initialize a password?");
            }

            return await Task.FromResult(new SaltsPackage()
            {
                SaltKek64 = Convert.ToBase64String(pk.SaltKek),
                SaltPassword64 = Convert.ToBase64String(pk.SaltPassword)
            });
        }

        public async Task<RsaFullKeyListData> GenerateOfflineRsaKeyList()
        {
            var rsaKeyList = RsaKeyListManagement.CreateRsaKeyList(RsaKeyListManagement.zeroSensitiveKey, RsaKeyListManagement.DefaultMaxOfflineKeys, RsaKeyListManagement.DefaultHoursOfflineKey); // TODO
            _tenantSystemStorage.SingleKeyValueStorage.Upsert(_rsaKeyStorageId, rsaKeyList);
            return await Task.FromResult(rsaKeyList);
        }

        public async Task UpdateKeyList()
        {
            await Task.CompletedTask;
        }

        public async Task<RsaFullKeyListData> GetOfflineRsaKeyList()
        {
            var result = _tenantSystemStorage.SingleKeyValueStorage.Get<RsaFullKeyListData>(_rsaKeyStorageId);

//            if (result == null || result.ListRSA == null) 
            if (result == null || result.ListRSA == null || result.ListRSA.Count == 0 || result.ListRSA.TrueForAll(x => x.IsDead()))
            {
                return await this.GenerateOfflineRsaKeyList();
            }

            return result;
        }

        // Given the client's nonce and nonceHash, load the identities passwordKey info
        // and with that info we can validate if the client calculated the right hash.
        public async Task TryPasswordKeyMatch(string nonceHashedPassword64, string nonce64)
        {
            var pk = _tenantSystemStorage.SingleKeyValueStorage.Get<PasswordData>(_passwordKey);

            // TODO XXX Where the heck do we validate the server has the nonce64 (prevent replay)

            PasswordDataManager.TryPasswordKeyMatch(pk, nonceHashedPassword64, nonce64);
            await Task.CompletedTask;
        }
    }
}