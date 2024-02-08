using System;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Odin.Core.Exceptions;
using Odin.Core.Services.Base;
using Odin.Core.Services.Drives;
using Odin.Core.Services.Mediator.Owner;
using Odin.Core.Services.Membership.Connections;
using Odin.Core.Services.Peer.Incoming.Drive.InboxStorage;
using Odin.Core.Services.Peer.Outgoing;

namespace Odin.Core.Services.Peer.Incoming.Drive
{
    public class TransitInboxProcessor(
        OdinContextAccessor contextAccessor,
        TransitInboxBoxStorage transitInboxBoxStorage,
        FileSystemResolver fileSystemResolver,
        TenantSystemStorage tenantSystemStorage,
        CircleNetworkService circleNetworkService)
        : INotificationHandler<RsaKeyRotatedNotification>
    {
        /// <summary>
        /// Processes incoming transfers by converting their transfer
        /// keys and moving files to long term storage.  Returns the number of items in the inbox
        /// </summary>
        public async Task<InboxStatus> ProcessInbox(TargetDrive targetDrive, int batchSize = 1)
        {
            var driveId = contextAccessor.GetCurrent().PermissionsContext.GetDriveId(targetDrive);
            var items = await transitInboxBoxStorage.GetPendingItems(driveId, batchSize);

            TransitFileWriter writer = new TransitFileWriter(contextAccessor, fileSystemResolver);

            foreach (var inboxItem in items)
            {
                using (tenantSystemStorage.CreateCommitUnitOfWork())
                {
                    try
                    {
                        var fs = fileSystemResolver.ResolveFileSystem(inboxItem.FileSystemType);

                        var tempFile = new InternalDriveFileId()
                        {
                            DriveId = inboxItem.DriveId,
                            FileId = inboxItem.FileId
                        };

                        if (inboxItem.InstructionType == TransferInstructionType.SaveFile)
                        {
                            var icr = await circleNetworkService.GetIdentityConnectionRegistration(inboxItem.Sender, overrideHack: true);
                            var sharedSecret = icr.CreateClientAccessToken(contextAccessor.GetCurrent().PermissionsContext.GetIcrKey()).SharedSecret;
                            var decryptedKeyHeader = inboxItem.SharedSecretEncryptedKeyHeader.DecryptAesToKeyHeader(ref sharedSecret);

                            await writer.HandleFile(tempFile, fs, decryptedKeyHeader, inboxItem.Sender, inboxItem.TransferInstructionSet);
                            // await writer.HandleFile(tempFile, fs, decryptedKeyHeader, inboxItem.Sender, inboxItem.FileSystemType, inboxItem.TransferFileType,);
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

                        await transitInboxBoxStorage.MarkComplete(inboxItem.DriveId, inboxItem.Marker);
                    }
                    catch (OdinRemoteIdentityException)
                    {
                        await transitInboxBoxStorage.MarkFailure(inboxItem.DriveId, inboxItem.Marker);
                        throw;
                    }
                    catch (Exception)
                    {
                        await transitInboxBoxStorage.MarkFailure(inboxItem.DriveId, inboxItem.Marker);
                    }
                }
            }

            return transitInboxBoxStorage.GetPendingCount(driveId);
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