using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Youverse.Core.Exceptions;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Drives;
using Youverse.Core.Services.Drives.DriveCore.Storage;
using Youverse.Core.Services.EncryptionKeyService;
using Youverse.Core.Services.Transit.Encryption;
using Youverse.Core.Services.Transit.ReceivingHost.Incoming;
using Youverse.Core.Services.Transit.SendingHost;

namespace Youverse.Core.Services.Transit.ReceivingHost
{
    public class TransitInboxProcessor : ITransitInboxProcessor
    {
        private readonly DotYouContextAccessor _contextAccessor;
        private readonly TransitInboxBoxStorage _transitInboxBoxStorage;
        private readonly FileSystemResolver _fileSystemResolver;
        private readonly IPublicKeyService _publicKeyService;
        
        public TransitInboxProcessor(DotYouContextAccessor contextAccessor,
            TransitInboxBoxStorage transitInboxBoxStorage,
            FileSystemResolver fileSystemResolver, IPublicKeyService publicKeyService)
        {
            _contextAccessor = contextAccessor;
            _transitInboxBoxStorage = transitInboxBoxStorage;
            _fileSystemResolver = fileSystemResolver;
            _publicKeyService = publicKeyService;
        }

        public async Task ProcessIncomingTransitInstructions(TargetDrive targetDrive)
        {
            var driveId = _contextAccessor.GetCurrent().PermissionsContext.GetDriveId(targetDrive);
            var items = await _transitInboxBoxStorage.GetPendingItems(driveId);

            TransitFileWriter writer = new TransitFileWriter(_contextAccessor, _fileSystemResolver);
            
            // var drivesNeedingACommit = new List<Guid>();
            foreach (var inboxItem in items)
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
                    // drivesNeedingACommit.Add(item.DriveId);
                    await fs.Query.EnsureDriveDatabaseCommits(new List<Guid>() { inboxItem.DriveId });

                    // var items2 = await GetAcceptedItems(drive);
                    //
                    // if (items2.Count > 0)
                    // {
                    //     string x = "";
                    // }
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

            // foreach (var x in drivesNeedingACommit)
            // {
            // }
        }

        public Task<PagedResult<TransferInboxItem>> GetQuarantinedItems(PageOptions pageOptions)
        {
            throw new NotImplementedException();
        }
    }
}