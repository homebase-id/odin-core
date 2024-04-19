using System;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.Extensions.Logging;
using Odin.Core.Exceptions;
using Odin.Core.Util;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.Mediator.Owner;
using Odin.Services.Membership.Connections;
using Odin.Services.Peer.Incoming.Drive.Transfer.InboxStorage;
using Odin.Services.Peer.Outgoing.Drive;

namespace Odin.Services.Peer.Incoming.Drive.Transfer
{
    public class PeerInboxProcessor(
        TransitInboxBoxStorage transitInboxBoxStorage,
        FileSystemResolver fileSystemResolver,
        TenantSystemStorage tenantSystemStorage,
        CircleNetworkService circleNetworkService,
        ILogger<PeerInboxProcessor> logger)
        : INotificationHandler<RsaKeyRotatedNotification>
    {
        /// <summary>
        /// Processes incoming transfers by converting their transfer
        /// keys and moving files to long term storage.  Returns the number of items in the inbox
        /// </summary>
        public async Task<InboxStatus> ProcessInbox(TargetDrive targetDrive, IOdinContext odinContext, int batchSize = 1)
        {
            var driveId = odinContext.PermissionsContext.GetDriveId(targetDrive);
            logger.LogDebug("Processing Inbox -> Getting Pending Items for drive {driveId} with batchSize: {batchSize}", driveId, batchSize);
            var items = await transitInboxBoxStorage.GetPendingItems(driveId, batchSize);

            PeerFileWriter writer = new PeerFileWriter(fileSystemResolver);
            logger.LogDebug("Processing Inbox -> Getting Pending Items returned: {itemCount}", items.Count);

            foreach (var inboxItem in items)
            {
                // logger.LogDebug("Processing Inbox -> CreateCommitUnitOfWork");

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
                            // logger.LogDebug("Processing Inbox -> GetIdentityConnectionRegistration");
                            var icr = await circleNetworkService.GetIdentityConnectionRegistration(inboxItem.Sender,odinContext, overrideHack: true);
                            var sharedSecret = icr.CreateClientAccessToken(odinContext.PermissionsContext.GetIcrKey()).SharedSecret;

                            // logger.LogDebug("Processing Inbox -> DecryptAesToKeyHeader");
                            var decryptedKeyHeader = inboxItem.SharedSecretEncryptedKeyHeader.DecryptAesToKeyHeader(ref sharedSecret);

                            // logger.LogDebug("Processing Inbox -> GetIdentityConnectionRegistration Complete;  Took {getIdentReg}", getIdentRegMs);
                            // logger.LogDebug("Processing Inbox -> handle file from sender:{sender}", inboxItem.Sender);

                            var handleFileMs = await Benchmark.MillisecondsAsync(async () =>
                            {
                                await writer.HandleFile(tempFile, fs, decryptedKeyHeader, inboxItem.Sender, inboxItem.TransferInstructionSet,odinContext);
                            });

                            logger.LogDebug("Processing Inbox -> HandleFile Complete. Took {ms} ms", handleFileMs);
                        }
                        else if (inboxItem.InstructionType == TransferInstructionType.DeleteLinkedFile)
                        {
                            // logger.LogDebug("Processing Inbox -> DeleteFile from sender:{sender}", inboxItem.Sender);
                            await writer.DeleteFile(fs, inboxItem,odinContext);
                            // logger.LogDebug("Processing Inbox -> DeleteFile done");
                        }
                        else if (inboxItem.InstructionType == TransferInstructionType.None)
                        {
                            throw new OdinClientException("Transfer type not specified", OdinClientErrorCode.TransferTypeNotSpecified);
                        }
                        else
                        {
                            throw new OdinClientException("Invalid transfer type", OdinClientErrorCode.InvalidTransferType);
                        }

                        // logger.LogDebug("Processing Inbox -> MarkComplete: marker: {marker} for drive: {driveId}", inboxItem.Marker, inboxItem.DriveId);
                        await transitInboxBoxStorage.MarkComplete(inboxItem.DriveId, inboxItem.Marker);
                    }
                    catch (OdinRemoteIdentityException)
                    {
                        // logger.LogDebug("Processing Inbox -> failed with remote identity exception: {message}", oe.Message);
                        // logger.LogDebug("Processing Inbox -> MarkFailure (remote identity exception): marker: {marker} for drive: {driveId}", inboxItem.Marker,
                        // inboxItem.DriveId);
                        await transitInboxBoxStorage.MarkFailure(inboxItem.DriveId, inboxItem.Marker);
                        throw;
                    }
                    catch (OdinFileWriteException)
                    {
                        logger.LogError("File was missing for inbox item.  the inbox item will be removed");
                        await transitInboxBoxStorage.MarkComplete(inboxItem.DriveId, inboxItem.Marker);
                    }
                    catch (Exception)
                    {
                        // logger.LogDebug("Processing Inbox -> failed with exception: {message}", e.Message);
                        // logger.LogDebug("Processing Inbox -> MarkFailure (general Exception): marker: {marker} for drive: {driveId}", inboxItem.Marker,
                        //     inboxItem.DriveId);
                        await transitInboxBoxStorage.MarkFailure(inboxItem.DriveId, inboxItem.Marker);
                    }
                }
            }

            var pendingCount = transitInboxBoxStorage.GetPendingCount(driveId);
            logger.LogDebug("Processing Inbox -> returning pending count.  total items: {pendingCount}, popped items: {popped}", pendingCount.TotalItems,
                pendingCount.PoppedCount);
            return pendingCount;
        }

        public Task Handle(RsaKeyRotatedNotification notification, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}