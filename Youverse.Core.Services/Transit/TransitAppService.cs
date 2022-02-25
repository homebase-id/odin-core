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
        private readonly DotYouContextAccessor _contextAccessor;
        private readonly IDriveService _driveService;
        private readonly ISystemStorage _systemStorage;
        private readonly ITransitBoxService _transitBoxService;
        private readonly IInboxService _inboxService;

        private readonly IAppRegistrationService _appRegistrationService;

        public TransitAppService(IDriveService driveService, DotYouContextAccessor contextAccessor, ISystemStorage systemStorage, IAppRegistrationService appRegistrationService, ITransitBoxService transitBoxService, IInboxService inboxService)
        {
            _driveService = driveService;
            _contextAccessor = contextAccessor;
            _systemStorage = systemStorage;
            _appRegistrationService = appRegistrationService;
            _transitBoxService = transitBoxService;
            _inboxService = inboxService;
        }

        public async Task StoreLongTerm(DriveFileId file)
        {
            var rsaKeyHeader = await _driveService.GetDeserializedStream<RsaEncryptedRecipientTransferKeyHeader>(file, MultipartHostTransferParts.TransferKeyHeader.ToString(), StorageDisposition.Temporary);

            var appId = _contextAccessor.GetCurrent().AppContext.AppId;
            var appKey = _contextAccessor.GetCurrent().AppContext.GetAppKey();

            var keys = await _appRegistrationService.GetRsaKeyList(appId);
            var pk = RsaKeyListManagement.FindKey(keys, rsaKeyHeader.PublicKeyCrc);

            if (pk == null)
            {
                throw new YouverseSecurityException("Invalid public key");
            }

            var decryptedPrivateKey = pk.Decrypt(ref appKey, rsaKeyHeader.EncryptedAesKey).ToSensitiveByteArray();
            var keyHeader = KeyHeader.FromCombinedBytes(decryptedPrivateKey.GetKey(), 16, 16);
            decryptedPrivateKey.Wipe();

            //TODO: this deserialization would be better in the drive service under the name GetTempMetadata or something
            var metadataStream = await _driveService.GetTempStream(file, MultipartHostTransferParts.Metadata.ToString().ToLower());
            var json = await new StreamReader(metadataStream).ReadToEndAsync();
            metadataStream.Close();
            var metadata = JsonConvert.DeserializeObject<FileMetadata>(json);
            metadata.SenderDotYouId = _contextAccessor.GetCurrent().Caller.DotYouId;

            await _driveService.StoreLongTerm(file, keyHeader, metadata, MultipartHostTransferParts.Payload.ToString());
        }

        public async Task ProcessTransfers()
        {
            //TODO: perform these in parallel
            var items = await GetAcceptedItems(PageOptions.All);
            foreach (var item in items.Results)
            {
                await StoreLongTerm(item.TempFile);
                await _inboxService.Add(new InboxItem()
                {
                    Sender = item.Sender,
                    AddedTimestamp = DateTimeExtensions.UnixTimeMilliseconds(),
                    AppId = item.AppId,
                    File = item.TempFile,
                    Priority = 0 //TODO
                });
                await _transitBoxService.Remove(item.AppId, item.Id);
            }
        }

        public async Task<PagedResult<TransferBoxItem>> GetAcceptedItems(PageOptions pageOptions)
        {
            var appId = _contextAccessor.GetCurrent().AppContext.AppId;
            return await _transitBoxService.GetPendingItems(appId, pageOptions);
        }

        public Task<PagedResult<TransferBoxItem>> GetQuarantinedItems(PageOptions pageOptions)
        {
            throw new NotImplementedException();
        }
    }
}