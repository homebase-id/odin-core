using System;
using System.Threading.Tasks;
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
        private readonly string _rsaKeyStoragePrefix = "tk";

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

        public async Task WriteTransferKeyHeaderAsLongTerm(DriveFileId file, RsaEncryptedRecipientTransferKeyHeader header)
        {
            var appId = _context.AppContext.AppId;
            var appKey = _context.AppContext.GetAppKey();

            var keys = await _appRegistrationService.GetRsaKeyList(appId);
            var pk = RsaKeyListManagement.FindKey(keys, header.PublicKeyCrc);

            if (pk == null)
            {
                throw new YouverseSecurityException("Invalid public key");
            }

            var decryptedBytes = pk.Decrypt(appKey, header.EncryptedAesKey).ToSensitiveByteArray(); // TODO
            var keyHeader = KeyHeader.FromCombinedBytes(decryptedBytes.GetKey(), 16, 16);
            decryptedBytes.Wipe();

            await _driveService.StoreLongTerm(keyHeader, metadata,);
            await _driveService.WriteKeyHeader(file, keyHeader);
        }

        public async Task ProcessRecentTransfers()
        {
            var items = await GetAcceptedItems(PageOptions.All);
            foreach (var item in items.Results)
            {
                var rsaKeyHeader = await _driveService.GetDeserializedStream<RsaEncryptedRecipientTransferKeyHeader>(item.TempFile, MultipartHostTransferParts.TransferKeyHeader.ToString(), StorageDisposition.Temporary);

                await WriteTransferKeyHeaderAsLongTerm(item.TempFile, rsaKeyHeader);
                var keyHeader =

                    //TODO: move payload and metadata to long term storage
                    await _driveService.StoreLongTerm(keyHeader, metadata, PayloadExtension);
            }
        }

        public async Task<PagedResult<TransferBoxItem>> GetAcceptedItems(PageOptions pageOptions)
        {
            return await _transitBoxService.GetPendingItems(pageOptions)
        }

        public Task<PagedResult<TransferBoxItem>> GetQuarantinedItems(PageOptions pageOptions)
        {
            throw new NotImplementedException();
        }
    }
}