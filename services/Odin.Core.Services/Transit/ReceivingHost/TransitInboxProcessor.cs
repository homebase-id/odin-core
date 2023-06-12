using System;
using System.Threading.Tasks;
using Odin.Core.Exceptions;
using Odin.Core.Services.Base;
using Odin.Core.Services.Drives;
using Odin.Core.Services.EncryptionKeyService;
using Odin.Core.Services.Transit.Encryption;
using Odin.Core.Services.Transit.ReceivingHost.Incoming;
using Odin.Core.Services.Transit.SendingHost;

namespace Odin.Core.Services.Transit.ReceivingHost
{
    public class TransitInboxProcessor
    {
        private readonly OdinContextAccessor _contextAccessor;
        private readonly TransitInboxBoxStorage _transitInboxBoxStorage;
        private readonly FileSystemResolver _fileSystemResolver;
        private readonly RsaKeyService _publicKeyService;
        private readonly TenantSystemStorage _tenantSystemStorage;

        public TransitInboxProcessor(OdinContextAccessor contextAccessor,
            TransitInboxBoxStorage transitInboxBoxStorage,
            FileSystemResolver fileSystemResolver, RsaKeyService publicKeyService, TenantSystemStorage tenantSystemStorage)
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
                            // var (isValidPublicKey, decryptedAesKeyHeaderBytes) =
                            //     await _publicKeyService.DecryptPayload(RsaKeyType.OnlineKey, inboxItem.RsaEncryptedKeyHeader, inboxItem.PublicKeyCrc);

                            var (isValidPublicKey, decryptedAesKeyHeaderBytes) =
                                await _publicKeyService.DecryptPayload(RsaKeyType.OnlineKey, inboxItem.RsaEncryptedKeyHeaderPayload);

                            if (!isValidPublicKey)
                            {
                                //TODO: handle when isValidPublicKey = false
                                throw new OdinSecurityException("Public key was invalid");
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
                            throw new OdinClientException("Transfer type not specified", OdinClientErrorCode.TransferTypeNotSpecified);
                        }
                        else
                        {
                            throw new OdinClientException("Invalid transfer type", OdinClientErrorCode.InvalidTransferType);
                        }

                        await _transitInboxBoxStorage.MarkComplete(inboxItem.DriveId, inboxItem.Marker);
                    }
                    catch (OdinRemoteIdentityException)
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