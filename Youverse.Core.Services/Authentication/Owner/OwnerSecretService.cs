using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Youverse.Core.Cryptography;
using Youverse.Core.Cryptography.Crypto;
using Youverse.Core.Cryptography.Data;
using Youverse.Core.Exceptions;
using Youverse.Core.Services.Base;
using Youverse.Core.Storage;

namespace Youverse.Core.Services.Authentication.Owner
{
    public class OwnerSecretService : IOwnerSecretService
    {
        private readonly GuidId _passwordKey = GuidId.FromString("_passwordKey");
        private readonly GuidId _rsaKeyStorageId = GuidId.FromString("_rsaKeyStorageId");

        private readonly ISystemStorage _systemStorage;

        public OwnerSecretService(TenantContext tenantContext, ISystemStorage systemStorage)
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
                _systemStorage.SingleKeyValueStorage.Upsert(_rsaKeyStorageId, rsaKeyList);
            }

            var nonce = NonceData.NewRandomNonce(key);
            _systemStorage.SingleKeyValueStorage.Upsert(nonce.Id, nonce);

            return nonce;
        }

        public async Task SetNewPassword(PasswordReply reply)
        {
            if (await IsMasterPasswordSet())
            {
                throw new YouverseSecurityException("Password already set");
            }

            Guid originalNoncePackageKey = new Guid(Convert.FromBase64String(reply.Nonce64));
            var originalNoncePackage = _systemStorage.SingleKeyValueStorage.Get<NonceData>(originalNoncePackageKey);

            //HACK: this will be moved to the overall provisioning process
            //await this.GenerateRsaKeyList();
            var keys = await this.GetRsaKeyList();

            var pk = PasswordDataManager.SetInitialPassword(originalNoncePackage, reply, keys);
            _systemStorage.SingleKeyValueStorage.Upsert(_passwordKey, pk);

            //delete the temporary salts
            _systemStorage.SingleKeyValueStorage.Delete(originalNoncePackageKey);
        }

        public Task<bool> IsMasterPasswordSet()
        {
            var existingPwd = _systemStorage.SingleKeyValueStorage.Get<PasswordData>(_passwordKey);
            return Task.FromResult(existingPwd != null);
        }

        public async Task<SensitiveByteArray> GetMasterKey(OwnerConsoleToken serverToken, SensitiveByteArray clientSecret)
        {
            var pk = _systemStorage.SingleKeyValueStorage.Get<PasswordData>(_passwordKey);
            if (null == pk)
            {
                throw new InvalidDataException("Secrets configuration invalid.  Did you initialize a password?");
            }

            //TODO: do we want to keep the extra layer of having a client and
            //server halfs to form the kek. then use that kek to decrypt the master key?
            var kek = serverToken.TokenEncryptedKek.DecryptKeyClone(ref clientSecret);

            var masterKey = pk.KekEncryptedMasterKey.DecryptKeyClone(ref kek);

            // masterKey.Wipe(); <- removed. The EncryptedDek class will zap this key on its destruction.
            serverToken.Dispose();

            return masterKey;
        }

        public async Task<SaltsPackage> GetStoredSalts()
        {
            var pk = _systemStorage.SingleKeyValueStorage.Get<PasswordData>(_passwordKey);

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
            _systemStorage.SingleKeyValueStorage.Upsert(_rsaKeyStorageId, rsaKeyList);
            return rsaKeyList;
        }

        public async Task<RsaFullKeyListData> GetRsaKeyList()
        {
            var result = _systemStorage.SingleKeyValueStorage.Get<RsaFullKeyListData>(_rsaKeyStorageId);

            if (result == null || result.ListRSA == null)
            {
                return await this.GenerateRsaKeyList();
            }

            return result;
        }

        // Given the client's nonce and nonceHash, load the identities passwordKey info
        // and with that info we can validate if the client calculated the right hash.
        public async Task TryPasswordKeyMatch(string nonceHashedPassword64, string nonce64)
        {
            var pk = _systemStorage.SingleKeyValueStorage.Get<PasswordData>(_passwordKey);

            // TODO XXX Where the heck do we validate the server has the nonce64 (prevent replay)

            PasswordDataManager.TryPasswordKeyMatch(pk, nonceHashedPassword64, nonce64);
        }
    }
}