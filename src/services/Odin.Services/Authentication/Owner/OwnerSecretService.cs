using System;
using System.Threading.Tasks;
using Odin.Core;
using Odin.Core.Cryptography.Crypto;
using Odin.Core.Cryptography.Data;
using Odin.Core.Cryptography.Login;
using Odin.Core.Exceptions;
using Odin.Core.Storage;
using Odin.Core.Storage.Database.Identity.Cache;
using Odin.Services.Base;
using Odin.Services.EncryptionKeyService;

namespace Odin.Services.Authentication.Owner
{
    public class OwnerSecretService(
        TenantContext tenantContext,
        PasswordKeyRecoveryService recoveryService,
        PublicPrivateKeyService publicPrivateKeyService,
        TableKeyValueCached tblKeyValue)
    {
        private static readonly Guid PasswordKeyStorageId = Guid.Parse("e0b5bb7d-f3a5-4388-b609-81fbf4b3b2f7");
        // private static readonly Guid RsaKeyStorageId = Guid.Parse("b5e1e0d0-2f27-429a-9c44-34cbcc71745e");
        private readonly Guid EccKeyStorageId = Guid.Parse("a54ee0f0-e861-45c4-90df-9402a383aac1");

        private const string NonceDataContextKey = "c45430e7-9c05-49fa-bc8b-d8c1f261f57e";
        private static readonly SingleKeyValueStorage NonceDataStorage = TenantSystemStorage.CreateSingleKeyValueStorage(Guid.Parse(NonceDataContextKey));

        private const string PasswordKeyContextKey = "febf5105-a2b3-4a17-937d-582ecd8a427b";
        private static readonly SingleKeyValueStorage PasswordDataStorage = TenantSystemStorage.CreateSingleKeyValueStorage(Guid.Parse(PasswordKeyContextKey));
        
        private const string EccKeyContextKey = "fb8d351e-2c4f-4f1d-9b84-aefe7bc11ee1";
        private static readonly SingleKeyValueStorage EccStorage = TenantSystemStorage.CreateSingleKeyValueStorage(Guid.Parse(EccKeyContextKey));


        /// <summary>
        /// Generates two 16 byte crypto-random numbers used for salting passwords
        /// </summary>
        public async Task<NonceData> GenerateNewSaltsAsync()
        {
            var eccKeyList = await GetOfflineEccKeyListAsync();
            var key = EccKeyListManagement.GetCurrentKey(eccKeyList);
            var nonce = NonceData.NewRandomNonce(key);
            await NonceDataStorage.UpsertAsync(tblKeyValue, nonce.Id, nonce);
            return nonce;
        }

        public async Task SetNewPasswordAsync(PasswordReply reply)
        {
            bool canSet = reply.FirstRunToken == tenantContext.FirstRunToken || tenantContext.IsPreconfigured;
            if (!canSet)
            {
                throw new OdinClientException("Invalid first run token; cannot set password", OdinClientErrorCode.PasswordAlreadySet);
            }

            if (await IsMasterPasswordSetAsync())
            {
                throw new OdinClientException("Password already set", OdinClientErrorCode.PasswordAlreadySet);
            }

            await SavePasswordAsync(reply);
        }

        /// <summary>
        /// Returns true if the master password has set
        /// </summary>
        public async Task<bool> IsMasterPasswordSetAsync()
        {
            var existingPwd = await PasswordDataStorage.GetAsync<PasswordData>(tblKeyValue, PasswordKeyStorageId);
            return existingPwd != null;
        }

        /// <summary>
        /// Returns the encrypted version of the data encryption key.  This is generated when you set
        /// the initial password
        /// </summary>
        public async Task<SensitiveByteArray> GetMasterKeyAsync(OwnerConsoleToken serverToken, SensitiveByteArray clientSecret)
        {
            var pk = await PasswordDataStorage.GetAsync<PasswordData>(tblKeyValue, PasswordKeyStorageId);
            if (null == pk)
            {
                throw new OdinClientException("Secrets configuration invalid. Did you initialize a password?");
            }

            //TODO: do we want to keep the extra layer of having a client and
            //server halfs to form the kek. then use that kek to decrypt the master key?
            var kek = serverToken.TokenEncryptedKek.DecryptKeyClone(clientSecret);

            var masterKey = pk.KekEncryptedMasterKey.DecryptKeyClone(kek);

            // masterKey.Wipe(); <- removed. The EncryptedDek class will zap this key on its destruction.
            serverToken.Dispose();

            return masterKey;
        }
        
        public async Task<(uint publicKeyCrc32C, string publicKeyJwk)> GetCurrentAuthenticationEccKeyAsync()
        {
            var eccKeyList = await this.GetOfflineEccKeyListAsync();
            var key = EccKeyListManagement.GetCurrentKey(eccKeyList);
            return (key.crc32c, key.PublicKeyJwk());
        }

        /// <summary>
        /// Returns the stored salts for the tenant
        /// </summary>
        public async Task<SaltsPackage> GetStoredSaltsAsync()
        {
            var pk = await PasswordDataStorage.GetAsync<PasswordData>(tblKeyValue, PasswordKeyStorageId);

            if (null == pk)
            {
                throw new OdinClientException("Secrets configuration invalid. Did you initialize a password?");
            }

            return new SaltsPackage
            {
                SaltKek64 = Convert.ToBase64String(pk.SaltKek),
                SaltPassword64 = Convert.ToBase64String(pk.SaltPassword)
            };
        }
        
        public async Task<EccFullKeyListData> GenerateOfflineEccKeyListAsync()
        {
            var eccKeyList = EccKeyListManagement.CreateEccKeyList(EccKeyListManagement.zeroSensitiveKey, EccKeyListManagement.DefaultMaxOfflineKeys,
                EccKeyListManagement.DefaultHoursOfflineKey); // TODO
            await EccStorage.UpsertAsync(tblKeyValue,  EccKeyStorageId, eccKeyList);
            return eccKeyList;
        }
        
        /// <summary>
        /// Gets the current Ecc Keys generated by <see cref="GenerateOfflineEccKeyListAsync"/>.
        /// </summary>
        public async Task<EccFullKeyListData> GetOfflineEccKeyListAsync()
        {
            var result = await EccStorage.GetAsync<EccFullKeyListData>(tblKeyValue, EccKeyStorageId);
            if (result == null || result.ListEcc == null || result.ListEcc.Count == 0 || result.ListEcc.TrueForAll(x => x.IsDead()))
            {
                return await this.GenerateOfflineEccKeyListAsync();
            }

            return result;
        }

        // Given the client's nonce and nonceHash, load the identities passwordKey info
        // and with that info we can validate if the client calculated the right hash.
        /// <summary>
        /// Checks if the nonce-hashed password matches the stored
        /// <see cref="PasswordData.HashPassword"/> (hashed with a <param name="nonce64">nonce</param>
        /// </summary>
        /// <param name="nonceHashedPassword64"></param>
        /// <param name="nonce64"></param>
        public async Task AssertPasswordKeyMatchAsync(string nonceHashedPassword64, string nonce64)
        {
            var pk = await PasswordDataStorage.GetAsync<PasswordData>(tblKeyValue, PasswordKeyStorageId);

            // TODO SECURITY
            // TODO XXX Where the heck do we validate the server has the nonce64 (prevent replay)

            PasswordDataManager.TryPasswordKeyMatch(pk, nonceHashedPassword64, nonce64);
        }

        public async Task ResetPasswordUsingRecoveryKeyAsync(ResetPasswordUsingRecoveryKeyRequest request, IOdinContext odinContext)
        {
            var decryptedBytes = await publicPrivateKeyService.EccDecryptPayload(request.EncryptedRecoveryKey, odinContext);

            var recoveryKey = decryptedBytes.ToStringFromUtf8Bytes();
            var masterKey = await recoveryService.AssertValidKeyAsync(recoveryKey);
            await SavePasswordAsync(request.PasswordReply, masterKey);
        }

        public async Task ResetPasswordAsync(ResetPasswordRequest request, IOdinContext odinContext)
        {
            odinContext.Caller.AssertHasMasterKey();
            
            await this.AssertPasswordKeyMatchAsync(request.CurrentAuthenticationPasswordReply.NonceHashedPassword64, request.CurrentAuthenticationPasswordReply.Nonce64);

            var masterKey = odinContext.Caller.GetMasterKey();
            await SavePasswordAsync(request.NewPasswordReply, masterKey);
        }

        private async Task SavePasswordAsync(PasswordReply reply, SensitiveByteArray masterKey = null)
        {
            Guid originalNoncePackageKey = new Guid(Convert.FromBase64String(reply.Nonce64));
            var originalNoncePackage = await NonceDataStorage.GetAsync<NonceData>(tblKeyValue, originalNoncePackageKey);

            var keys = await this.GetOfflineEccKeyListAsync();

            PasswordData pk = PasswordDataManager.SetInitialPassword(originalNoncePackage, reply, keys, masterKey);
            await PasswordDataStorage.UpsertAsync(tblKeyValue, PasswordKeyStorageId, pk);

            //delete the temporary salts
            await NonceDataStorage.DeleteAsync(tblKeyValue, originalNoncePackageKey);
        }
    }
}