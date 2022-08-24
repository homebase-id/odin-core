using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Youverse.Core.Cryptography;
using Youverse.Core.Cryptography.Crypto;
using Youverse.Core.Exceptions;
using Youverse.Core.Serialization;
using Youverse.Core.Services.Authentication;
using Youverse.Core.Services.Authorization.Acl;
using Youverse.Core.Services.Authorization.Apps;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Drive;
using Youverse.Core.Services.Drive.Storage;
using Youverse.Core.Services.EncryptionKeyService;
using Youverse.Core.Services.Transit.Encryption;
using Youverse.Core.Services.Transit.Incoming;
using Youverse.Core.Storage;

namespace Youverse.Core.Services.Transit
{
    public class TransitAppService : ITransitAppService
    {
        private readonly DotYouContextAccessor _contextAccessor;
        private readonly IDriveService _driveService;
        private readonly ISystemStorage _systemStorage;
        private readonly ITransitBoxService _transitBoxService;
        private readonly IInboxService _inboxService;
        private readonly IPublicKeyService _publicKeyService;

        private readonly IAppRegistrationService _appRegistrationService;

        public TransitAppService(IDriveService driveService, DotYouContextAccessor contextAccessor, ISystemStorage systemStorage, IAppRegistrationService appRegistrationService,
            ITransitBoxService transitBoxService, IInboxService inboxService, IPublicKeyService publicKeyService)
        {
            _driveService = driveService;
            _contextAccessor = contextAccessor;
            _systemStorage = systemStorage;
            _appRegistrationService = appRegistrationService;
            _transitBoxService = transitBoxService;
            _inboxService = inboxService;
            _publicKeyService = publicKeyService;
        }

        public async Task StoreLongTerm(TransferBoxItem item)
        {
            var file = item.TempFile;

            var transferInstructionSet =
                await _driveService.GetDeserializedStream<RsaEncryptedRecipientTransferInstructionSet>(file, MultipartHostTransferParts.TransferKeyHeader.ToString(), StorageDisposition.Temporary);

            var (isValidPublicKey, decryptedAesKeyHeaderBytes) =
                await _publicKeyService.DecryptKeyHeaderUsingOfflineKey(transferInstructionSet.EncryptedAesKeyHeader, transferInstructionSet.PublicKeyCrc);

            if (!isValidPublicKey)
            {
                //TODO: handle when isValidPublicKey = false
                throw new YouverseSecurityException("Public key was invalid");
            }

            var keyHeader = KeyHeader.FromCombinedBytes(decryptedAesKeyHeaderBytes, 16, 16);
            decryptedAesKeyHeaderBytes.WriteZeros();

            // var keyHeader = KeyHeader.FromCombinedBytes(transferInstructionSet.EncryptedAesKeyHeader, 16, 16);

            // var decryptedClientAuthTokenBytes = pk.Decrypt(ref appKey, transferInstructionSet.EncryptedClientAuthToken).ToSensitiveByteArray();
            // var clientAuthToken = ClientAuthToken.Parse(decryptedClientAuthTokenBytes.GetKey().StringFromUTF8Bytes());
            // decryptedClientAuthTokenBytes.Wipe();

            // transferInstructionSet.DriveAlias

            //TODO: this deserialization would be better in the drive service under the name GetTempMetadata or something
            var metadataStream = await _driveService.GetTempStream(file, MultipartHostTransferParts.Metadata.ToString().ToLower());
            var json = await new StreamReader(metadataStream).ReadToEndAsync();
            metadataStream.Close();

            var metadata = DotYouSystemSerializer.Deserialize<FileMetadata>(json);
            metadata!.SenderDotYouId = item.Sender;

            var serverMetadata = new ServerMetadata()
            {
                //files coming from other systems are only accessible to the owner so the owner can use the UI to pass the file along

                AccessControlList = new AccessControlList()
                {
                    RequiredSecurityGroup = SecurityGroupType.Owner
                },
            };

            await _driveService.CommitTempFileToLongTerm(file, keyHeader, metadata, serverMetadata, MultipartHostTransferParts.Payload.ToString());
        }

        public async Task ProcessTransfers()
        {
            //TODO: perform these in parallel
            var items = await GetAcceptedItems(PageOptions.All);
            foreach (var item in items.Results)
            {
                await StoreLongTerm(item);

                var externalFileIdentifier = new ExternalFileIdentifier()
                {
                    TargetDrive = _driveService.GetDrive(item.TempFile.DriveId).Result.TargetDriveInfo,
                    FileId = item.TempFile.FileId
                };

                await _inboxService.Add(new InboxItem()
                {
                    Sender = item.Sender,
                    AddedTimestamp = DateTimeExtensions.UnixTimeMilliseconds(),
                    File = externalFileIdentifier,
                    Priority = 0 //TODO
                });
                
                await _transitBoxService.Remove(item.TempFile.DriveId, item.Id);
            }
        }

        public async Task<PagedResult<TransferBoxItem>> GetAcceptedItems(PageOptions pageOptions)
        {
            //HACK: loop thru all drives until we put in place the new inbox/outbox solution
            var allDrives = await _driveService.GetDrives(PageOptions.All);

            var list = new List<TransferBoxItem>();
            foreach (var drive in allDrives.Results)
            {
                var l = await _transitBoxService.GetPendingItems(drive.Id, pageOptions);
                list.AddRange(l.Results);
            }

            return new PagedResult<TransferBoxItem>(pageOptions, 1, list);

            // var appId = _contextAccessor.GetCurrent().AppContext.AppId;
            // return await _transitBoxService.GetPendingItems(appId, pageOptions);
        }

        public Task<PagedResult<TransferBoxItem>> GetQuarantinedItems(PageOptions pageOptions)
        {
            throw new NotImplementedException();
        }
    }
}