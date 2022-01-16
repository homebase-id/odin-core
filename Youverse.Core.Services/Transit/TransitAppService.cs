using System;
using System.IO;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Youverse.Core.Cryptography;
using Youverse.Core.Cryptography.Crypto;
using Youverse.Core.Exceptions;
using Youverse.Core.Services.Authorization.Apps;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Drive;
using Youverse.Core.Services.Drive.Storage;
using Youverse.Core.Services.Transit.Encryption;
using Youverse.Core.Services.Transit.Incoming;

namespace Youverse.Core.Services.Transit
{
    public class TransitAppService : ITransitAppService
    {
        private readonly DotYouContext _context;
        private readonly IDriveService _driveService;
        private readonly ISystemStorage _systemStorage;
        private readonly ITransitBoxService _transitBoxService;

        private readonly IAppRegistrationService _appRegistrationService;

        public TransitAppService(IDriveService driveService, DotYouContext context, ISystemStorage systemStorage, IAppRegistrationService appRegistrationService, ITransitBoxService transitBoxService)
        {
            _driveService = driveService;
            _context = context;
            _systemStorage = systemStorage;
            _appRegistrationService = appRegistrationService;
            _transitBoxService = transitBoxService;
        }

        public async Task StoreLongTerm(DriveFileId file)
        {
            var rsaKeyHeader = await _driveService.GetDeserializedStream<RsaEncryptedRecipientTransferKeyHeader>(file, MultipartHostTransferParts.TransferKeyHeader.ToString(), StorageDisposition.Temporary);

            var appId = _context.AppContext.AppId;
            var appKey = _context.AppContext.GetAppKey();

            var keys = await _appRegistrationService.GetRsaKeyList(appId);
            var pk = RsaKeyListManagement.FindKey(keys, rsaKeyHeader.PublicKeyCrc);

            if (pk == null)
            {
                throw new YouverseSecurityException("Invalid public key");
            }

            var decryptedPrivateKey = pk.Decrypt(appKey, rsaKeyHeader.EncryptedAesKey).ToSensitiveByteArray(); // TODO
            var keyHeader = KeyHeader.FromCombinedBytes(decryptedPrivateKey.GetKey(), 16, 16);
            decryptedPrivateKey.Wipe();

            //TODO: this deserialization would be better int he drive service under the name GetTempMetadata or something
            var metadataStream = await _driveService.GetTempStream(file, MultipartHostTransferParts.Metadata.ToString().ToLower());
            var json = await new StreamReader(metadataStream).ReadToEndAsync();
            metadataStream.Close();
            var metadata = JsonConvert.DeserializeObject<FileMetadata>(json);

            await _driveService.StoreLongTerm(keyHeader, metadata, MultipartHostTransferParts.Payload.ToString().ToString());
        }

        public async Task ProcessRecentTransfers()
        {
            //TODO: perform these in parallel
            var items = await GetAcceptedItems(PageOptions.All);
            foreach (var item in items.Results)
            {
                await StoreLongTerm(item.TempFile);
            }
        }

        public async Task<PagedResult<TransferBoxItem>> GetAcceptedItems(PageOptions pageOptions)
        {
            return await _transitBoxService.GetPendingItems(pageOptions);
        }

        public Task<PagedResult<TransferBoxItem>> GetQuarantinedItems(PageOptions pageOptions)
        {
            throw new NotImplementedException();
        }
    }
}