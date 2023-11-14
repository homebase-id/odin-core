using System;
using System.Threading.Tasks;
using Odin.Core.Cryptography;
using Odin.Core.Cryptography.Crypto;
using Odin.Core.Cryptography.Data;
using Odin.Core.Exceptions;
using Odin.Core.Services.Base;
using Odin.Core.Services.EncryptionKeyService;
using Odin.Core.Storage;

namespace Odin.Core.Services.Authentication.Owner
{
    public class OwnerSecretService
    {
        private readonly Guid _passwordKeyStorageId = Guid.Parse("e0b5bb7d-f3a5-4388-b609-81fbf4b3b2f7");
        private readonly Guid _rsaKeyStorageId = Guid.Parse("b5e1e0d0-2f27-429a-9c44-34cbcc71745e");

        private readonly TenantContext _tenantContext;

        private readonly PublicPrivateKeyService _publicPrivateKeyService;
        private readonly RecoveryService _recoveryService;

        private readonly OdinContextAccessor _contextAccessor;

        private readonly SingleKeyValueStorage _nonceDataStorage;
        private readonly SingleKeyValueStorage _passwordDataStorage;
        private readonly SingleKeyValueStorage _rsaStorage;

        public OwnerSecretService(TenantContext tenantContext, TenantSystemStorage tenantSystemStorage, RecoveryService recoveryService,
            PublicPrivateKeyService publicPrivateKeyService, OdinContextAccessor contextAccessor)
        {
            _tenantContext = tenantContext;
            _recoveryService = recoveryService;
            _publicPrivateKeyService = publicPrivateKeyService;
            _contextAccessor = contextAccessor;
            
            const string nonceDataContextKey = "c45430e7-9c05-49fa-bc8b-d8c1f261f57e";
            _nonceDataStorage = tenantSystemStorage.CreateSingleKeyValueStorage(Guid.Parse(nonceDataContextKey));
            
            const string passwordKeyContextKey = "febf5105-a2b3-4a17-937d-582ecd8a427b";
            _passwordDataStorage = tenantSystemStorage.CreateSingleKeyValueStorage(Guid.Parse(passwordKeyContextKey));

            const string rsaKeyContextKey = "8caa641d-6346-4845-a859-fae9af4ab19b";
            _rsaStorage = tenantSystemStorage.CreateSingleKeyValueStorage(Guid.Parse(rsaKeyContextKey));
        }

        /// <summary>
        /// Generates two 16 byte crypto-random numbers used for salting passwords
        /// </summary>
        public async Task<NonceData> GenerateNewSalts()
        {
            var rsaKeyList = await this.GetOfflineRsaKeyList();
            var key = RsaKeyListManagement.GetCurrentKey(rsaKeyList);
            var nonce = NonceData.NewRandomNonce(key);
            _nonceDataStorage.Upsert(nonce.Id, nonce);

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

            await SavePassword(reply);
        }


        /// <summary>
        /// Returns true if the master password has set
        /// </summary>
        public Task<bool> IsMasterPasswordSet()
        {
            var existingPwd = _passwordDataStorage.Get<PasswordData>(_passwordKeyStorageId);
            return Task.FromResult(existingPwd != null);
        }

        /// <summary>
        /// Returns the encrypted version of the data encryption key.  This is generated when you set
        /// the initial password
        /// </summary>
        public async Task<SensitiveByteArray> GetMasterKey(OwnerConsoleToken serverToken, SensitiveByteArray clientSecret)
        {
            var pk = _passwordDataStorage.Get<PasswordData>(_passwordKeyStorageId);
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

        /// <summary>
        /// Gets the current RSA to be used for Authentication
        /// </summary>
        public async Task<(uint publicKeyCrc32C, string publicKeyPem)> GetCurrentAuthenticationRsaKey()
        {
            var rsaKeyList = await this.GetOfflineRsaKeyList();
            var key = RsaKeyListManagement.GetCurrentKey(rsaKeyList);
            return (key.crc32c, key.publicPem());
        }

        /// <summary>
        /// Returns the stored salts for the tenant
        /// </summary>
        public async Task<SaltsPackage> GetStoredSalts()
        {
            var pk = _passwordDataStorage.Get<PasswordData>(_passwordKeyStorageId);

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

        /// <summary>
        /// Generates RSA keys to be used for encrypting data where the private key is not
        /// encrypted on the server. (i.e. it should be stored securely in the same way you
        /// store the private key for an SSL cert)
        /// </summary>
        public async Task<RsaFullKeyListData> GenerateOfflineRsaKeyList()
        {
            var rsaKeyList = RsaKeyListManagement.CreateRsaKeyList(RsaKeyListManagement.zeroSensitiveKey, RsaKeyListManagement.DefaultMaxOfflineKeys,
                RsaKeyListManagement.DefaultHoursOfflineKey); // TODO
            _rsaStorage.Upsert(_rsaKeyStorageId, rsaKeyList);
            return await Task.FromResult(rsaKeyList);
        }

        /// <summary>
        /// Gets the current RSA Keys generated by <see cref="GenerateOfflineRsaKeyList"/>.
        /// </summary>
        public async Task<RsaFullKeyListData> GetOfflineRsaKeyList()
        {
            var result = _rsaStorage.Get<RsaFullKeyListData>(_rsaKeyStorageId);

            if (result == null || result.ListRSA == null || result.ListRSA.Count == 0 || result.ListRSA.TrueForAll(x => x.IsDead()))
            {
                return await this.GenerateOfflineRsaKeyList();
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
        public async Task AssertPasswordKeyMatch(string nonceHashedPassword64, string nonce64)
        {
            var pk = _passwordDataStorage.Get<PasswordData>(_passwordKeyStorageId);

            // TODO XXX Where the heck do we validate the server has the nonce64 (prevent replay)

            PasswordDataManager.TryPasswordKeyMatch(pk, nonceHashedPassword64, nonce64);
            await Task.CompletedTask;
        }

        public async Task ResetPasswordUsingRecoveryKey(ResetPasswordUsingRecoveryKeyRequest request)
        {
            var (isValidPublicKey, decryptedBytes) = await _publicPrivateKeyService.RsaDecryptPayload(RsaKeyType.OfflineKey, request.EncryptedRecoveryKey);

            if (!isValidPublicKey)
            {
                throw new OdinClientException("Invalid public key");
            }

            var recoveryKey = decryptedBytes.ToStringFromUtf8Bytes();
            _recoveryService.AssertValidKey(recoveryKey, out var masterKey);
            await SavePassword(request.PasswordReply, masterKey);
        }

        public async Task ResetPassword(ResetPasswordRequest request)
        {
            _contextAccessor.GetCurrent().Caller.AssertHasMasterKey();
            
            await this.AssertPasswordKeyMatch(request.CurrentAuthenticationPasswordReply.NonceHashedPassword64, request.CurrentAuthenticationPasswordReply.Nonce64);

            var masterKey = _contextAccessor.GetCurrent().Caller.GetMasterKey();
            await SavePassword(request.NewPasswordReply, masterKey);
        }

        private async Task SavePassword(PasswordReply reply, SensitiveByteArray masterKey = null)
        {
            Guid originalNoncePackageKey = new Guid(Convert.FromBase64String(reply.Nonce64));
            var originalNoncePackage = _nonceDataStorage.Get<NonceData>(originalNoncePackageKey);

            var keys = await this.GetOfflineRsaKeyList();

            PasswordData pk = PasswordDataManager.SetInitialPassword(originalNoncePackage, reply, keys, masterKey);
            _passwordDataStorage.Upsert(_passwordKeyStorageId, pk);

            //delete the temporary salts
            _nonceDataStorage.Delete(originalNoncePackageKey);
        }
    }
}