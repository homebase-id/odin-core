using System;
using System.Threading.Tasks;
using Youverse.Core.Cryptography.Crypto;
using Youverse.Core.Cryptography.Data;
using Youverse.Core.Services.Base;

namespace Youverse.Core.Services.EncryptionKeyService
{
    public interface IPublicKeyService
    {
        Task<byte[]> GetOfflinePublicKey();
    
        Task<byte[]> GetOnlinePublicKey();
    }
    
    public class PublicKeyService : IPublicKeyService
    {
        protected const string RSA_OFFLINE_KEY_STORAGE = "rks";
        protected readonly Guid RSA_KEY_STORAGE_ID = Guid.Parse("FFFFFFCF-0f85-DDDD-a7eb-e8e0b06c2555");
    
        protected readonly ISystemStorage _systemStorage;
    
        public PublicKeyService(ISystemStorage systemStorage)
        {
            _systemStorage = systemStorage;
        }
    
        public async Task<byte[]> GetOfflinePublicKey()
        {
            throw new System.NotImplementedException();
        }
    
        public async Task<byte[]> GetOnlinePublicKey()
        {
            var rsaKeyList = await this.GetRsaKeyList(RSA_OFFLINE_KEY_STORAGE);
    
            var key = RsaKeyListManagement.GetCurrentKey(ref RsaKeyListManagement.zeroSensitiveKey, ref rsaKeyList, out var keyListWasUpdated); // TODO
            if (keyListWasUpdated)
            {
                _systemStorage.WithTenantSystemStorage<RsaFullKeyListData>(RSA_OFFLINE_KEY_STORAGE, s => s.Save(rsaKeyList));
            }
    
            return key.publicKey;
        }
    
    
        private async Task<RsaFullKeyListData> GetRsaKeyList(string storage)
        {
            var result = await _systemStorage.WithTenantSystemStorageReturnSingle<RsaFullKeyListData>(storage, s => s.Get(RSA_KEY_STORAGE_ID));
    
            if (result == null || result.ListRSA == null)
            {
                return await this.GenerateRsaKeyList(storage);
            }
    
            return result;
        }
    
        private async Task<RsaFullKeyListData> GenerateRsaKeyList(string storage)
        {
            throw new NotImplementedException();
            // const int MAX_KEYS = 2; //leave this size 
            //
            // var rsaKeyList = RsaKeyListManagement.CreateRsaKeyList(ref RsaKeyListManagement.zeroSensitiveKey, MAX_KEYS); // TODO
            // rsaKeyList.Id = RSA_KEY_STORAGE_ID;
            //
            // var rsaKeyList = RsaKeyListManagement.CreateRsaKeyList(ref appKey, maxKeys);
            // appKey.Wipe();
            //
            // _systemStorage.WithTenantSystemStorage<RsaFullKeyListData>(storage, s => s.Save(rsaKeyList));
            // return rsaKeyList;
        }
    }
}