using System;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Odin.Core.Exceptions;
using Odin.Core.Services.Base;
using Odin.Core.Services.Contacts.Circle.Membership;
using Odin.Core.Services.Drives;
using Odin.Core.Services.Mediator.Owner;
using Odin.Core.Services.Transit.ReceivingHost.Incoming;
using Odin.Core.Services.Transit.SendingHost;

namespace Odin.Core.Services.Transit.ReceivingHost
{
    public class TransitInboxProcessor : INotificationHandler<RsaKeyRotatedNotification>
    {
        private readonly OdinContextAccessor _contextAccessor;
        private readonly TransitInboxBoxStorage _transitInboxBoxStorage;
        private readonly FileSystemResolver _fileSystemResolver;
        private readonly TenantSystemStorage _tenantSystemStorage;
        private readonly CircleNetworkService _circleNetworkService;

        public TransitInboxProcessor(OdinContextAccessor contextAccessor,
            TransitInboxBoxStorage transitInboxBoxStorage,
            FileSystemResolver fileSystemResolver,
            TenantSystemStorage tenantSystemStorage, CircleNetworkService circleNetworkService)
        {
            _contextAccessor = contextAccessor;
            _transitInboxBoxStorage = transitInboxBoxStorage;
            _fileSystemResolver = fileSystemResolver;
            _tenantSystemStorage = tenantSystemStorage;
            _circleNetworkService = circleNetworkService;
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
                            var icr = await _circleNetworkService.GetIdentityConnectionRegistration(inboxItem.Sender, overrideHack: true);
                            var sharedSecret = icr.CreateClientAccessToken(_contextAccessor.GetCurrent().PermissionsContext.GetIcrKey).SharedSecret;
                            var decryptedKeyHeader = inboxItem.SharedSecretEncryptedKeyHeader.DecryptAesToKeyHeader(ref sharedSecret);

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

        public Task Handle(RsaKeyRotatedNotification notification, CancellationToken cancellationToken)
        {
            // if (notification.KeyType == RsaKeyType.OnlineKey)
            // {
            //     var decryptionKey = _contextAccessor.GetCurrent().Caller.GetMasterKey();
            //
            //     foreach (var expiredKey in notification.ExpiredKeys)
            //     {
            //         //Get all items with expired keys
            //         var itemsWithExpiredKeys = _transitInboxBoxStorage.GetItemsByPublicKey(expiredKey.crc32c).GetAwaiter().GetResult();
            //
            //         foreach (var item in itemsWithExpiredKeys)
            //         {
            //             var newPayload = _rsaKeyService.UpgradeRsaKey(RsaKeyType.OnlineKey, expiredKey,
            //                 decryptionKey, item.RsaEncryptedKeyHeaderPayload).GetAwaiter().GetResult();
            //             _transitInboxBoxStorage.UpdateRsaPayload(item.FileId, newPayload);
            //         }
            //     }
            // }

            return Task.CompletedTask;
        }
    }
}