using System;
using System.Threading.Tasks;
using Youverse.Core.Cryptography;
using Youverse.Core.Cryptography.Crypto;
using Youverse.Core.Cryptography.Data;
using Youverse.Core.Exceptions;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Drive;
using Youverse.Core.Services.Transit.Encryption;

namespace Youverse.Core.Services.Apps
{
    public class AppService : IAppService
    {
        private readonly Guid _rsaKeyStorageId = Guid.Parse("FFFFFFCF-0f85-DDDD-a7eb-e8e0b06c2555");
        private readonly string _rsaKeyStorage = "app_transit_keys";

        private readonly DotYouContext _context;
        private readonly IDriveService _driveService;
        private readonly ISystemStorage _systemStorage;

        public AppService(IDriveService driveService, DotYouContext context, ISystemStorage systemStorage)
        {
            _driveService = driveService;
            _context = context;
            _systemStorage = systemStorage;
        }

        public async Task<ClientFileHeader> GetClientEncryptedFileHeader(DriveFileId file)
        {
            var ekh = await _driveService.GetEncryptedKeyHeader(file);
            var storageKey = _context.AppContext.GetDriveStorageKey(file.DriveId);
            
            var keyHeader = ekh.DecryptAesToKeyHeader(storageKey.GetKey());
            var appEkh = EncryptedKeyHeader.EncryptKeyHeaderAes(keyHeader, ekh.Iv, _context.AppContext.GetClientSharedSecret().GetKey());

            var md = await _driveService.GetMetadata(file);

            return new ClientFileHeader()
            {
                EncryptedKeyHeader = appEkh,
                FileMetadata = md
            };
        }
        
        /// <summary>
        /// Converts a transfer key header to a long term key header and stores it for the specified file.
        /// </summary>
        public async Task<EncryptedKeyHeader> WriteUploadKeyHeader(DriveFileId file, EncryptedKeyHeader sharedSecretEncryptedKeyHeader)
        {
            var sharedSecret = _context.AppContext.GetClientSharedSecret().GetKey();
            var kh = sharedSecretEncryptedKeyHeader.DecryptAesToKeyHeader(sharedSecret);
            return await _driveService.WriteKeyHeader(file, kh);
        }
        
        public async Task<TransitPublicKey> GetTransitPublicKey(Guid appid, uint? crc = null)
        {
            var rsaKeyList = await this.GetRsaKeyList();
            var key = RsaKeyListManagement.GetCurrentKey(Guid.Empty.ToByteArray().ToSensitiveByteArray(), ref rsaKeyList, out var keyListWasUpdated); // TODO

            if (keyListWasUpdated)
            {
                _systemStorage.WithTenantSystemStorage<RsaKeyListData>(_rsaKeyStorage, s => s.Save(rsaKeyList));
            }

            return new TransitPublicKey
            {
                PublicKey = key.publicKey,
                Expiration = key.expiration,
                Crc = key.crc32c
            };
        }

        public async Task WriteTransferKeyHeader(DriveFileId file, RsaEncryptedRecipientTransferKeyHeader header)
        {
            var keys = await GetRsaKeyList();
            var pk = RsaKeyListManagement.FindKey(keys, header.PublicKeyCrc);

            if (pk == null)
            {
                throw new YouverseSecurityException("Invalid public key");
            }

            var appKey = _context.AppContext.GetAppKey();
            var decryptedBytes = pk.Decrypt(appKey, header.EncryptedAesKey).ToSensitiveByteArray(); // TODO
            var keyHeader = KeyHeader.FromCombinedBytes(decryptedBytes.GetKey(), 16, 16);
            decryptedBytes.Wipe();
            
            await _driveService.WriteKeyHeader(file, keyHeader);
        }

        private async Task<RsaKeyListData> GetRsaKeyList()
        {
            var result = await _systemStorage.WithTenantSystemStorageReturnSingle<RsaKeyListData>(_rsaKeyStorage, s => s.Get(_rsaKeyStorageId));

            if (result == null)
            {
                const int MAX_KEYS = 4; //leave this size 

                var appKey = Guid.Empty.ToByteArray().ToSensitiveByteArray();
                var rsaKeyList = RsaKeyListManagement.CreateRsaKeyList(appKey, MAX_KEYS); // TODO
                rsaKeyList.Id = _rsaKeyStorageId;

                _systemStorage.WithTenantSystemStorage<RsaKeyListData>(_rsaKeyStorage, s => s.Save(rsaKeyList));

                result = rsaKeyList;
            }

            return result;
        }
        
    }
}