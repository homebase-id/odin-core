using System;
using System.Threading.Tasks;
using Youverse.Core.Exceptions;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Drives;
using Youverse.Core.Services.EncryptionKeyService;
using Youverse.Core.Services.Transit.Encryption;
using Youverse.Core.Services.Transit.ReceivingHost.Incoming;
using Youverse.Core.Services.Transit.SendingHost;
using Youverse.Core.Storage;

namespace Youverse.Core.Services.Transit.ReceivingHost
{
    public class TransitInboxProcessor 
    {
        private readonly DotYouContextAccessor _contextAccessor;
        private readonly TransitInboxBoxStorage _transitInboxBoxStorage;
        private readonly FileSystemResolver _fileSystemResolver;
        private readonly IPublicKeyService _publicKeyService;
        private readonly TenantSystemStorage _tenantSystemStorage;

        public TransitInboxProcessor(DotYouContextAccessor contextAccessor,
            TransitInboxBoxStorage transitInboxBoxStorage,
            FileSystemResolver fileSystemResolver, IPublicKeyService publicKeyService, TenantSystemStorage tenantSystemStorage)
        {
            _contextAccessor = contextAccessor;
            _transitInboxBoxStorage = transitInboxBoxStorage;
            _fileSystemResolver = fileSystemResolver;
            _publicKeyService = publicKeyService;
            _tenantSystemStorage = tenantSystemStorage;
        }

        /// <summary>
        /// Processes incoming transfers by converting their transfer
        /// keys and moving files to long term storage.  Returns the number of items in the inbox
        /// </summary>
        public async Task<InboxStatus> ProcessInbox(TargetDrive targetDrive, int batchSize = 1)
        {
            var driveId = _contextAccessor.GetCurrent().PermissionsContext.GetDriveId(targetDrive);
            var items = await _transitInboxBoxStorage.GetPendingItems(driveId, batchSize);

            TransitFileWriter writer = new TransitFileWriter(_contextAccessor, _fileSystemResolver);

            foreach (var inboxItem in items)
            {
                using (_tenantSystemStorage.CreateCommitUnitOfWork())
                {
                    try
                    {
                        var fs = _fileSystemResolver.ResolveFileSystem(inboxItem.FileSystemType);

                        var tempFile = new InternalDriveFileId()
                        {
                            DriveId = inboxItem.DriveId,
                            FileId = inboxItem.FileId
                        };

                        if (inboxItem.InstructionType == TransferInstructionType.SaveFile)
                        {
                            var (isValidPublicKey, decryptedAesKeyHeaderBytes) =
                                await _publicKeyService.DecryptKeyHeaderUsingOfflineKey(inboxItem.RsaEncryptedKeyHeader,
                                    inboxItem.PublicKeyCrc);

                            if (!isValidPublicKey)
                            {
                                //TODO: handle when isValidPublicKey = false
                                throw new YouverseSecurityException("Public key was invalid");
                            }

                            var decryptedKeyHeader = KeyHeader.FromCombinedBytes(decryptedAesKeyHeaderBytes);
                            decryptedAesKeyHeaderBytes.WriteZeros();

                            await writer.HandleFile(tempFile, fs, decryptedKeyHeader, inboxItem.Sender, inboxItem.FileSystemType, inboxItem.TransferFileType);
                        }
                        else if (inboxItem.InstructionType == TransferInstructionType.DeleteLinkedFile)
                        {
                            await writer.DeleteFile(fs, inboxItem);
                        }
                        else if (inboxItem.InstructionType == TransferInstructionType.None)
                        {
                            throw new YouverseClientException("Transfer type not specified", YouverseClientErrorCode.TransferTypeNotSpecified);
                        }
                        else
                        {
                            throw new YouverseClientException("Invalid transfer type", YouverseClientErrorCode.InvalidTransferType);
                        }

                        await _transitInboxBoxStorage.MarkComplete(inboxItem.DriveId, inboxItem.Marker);
                    }
                    catch (YouverseRemoteIdentityException)
                    {
                        await _transitInboxBoxStorage.MarkFailure(inboxItem.DriveId, inboxItem.Marker);
                        throw;
                    }
                    catch (Exception)
                    {
                        await _transitInboxBoxStorage.MarkFailure(inboxItem.DriveId, inboxItem.Marker);
                    }
                }
            }

            return _transitInboxBoxStorage.GetPendingCount(driveId);
        }

        public Task<PagedResult<TransferInboxItem>> GetQuarantinedItems(PageOptions pageOptions)
        {
            throw new NotImplementedException();
        }
    }
}