using System;
using System.Threading;
using System.Threading.Tasks;
using Bitcoin.BitcoinUtilities;
using MediatR;
using Microsoft.Extensions.Logging;
using Odin.Core;
using Odin.Core.Exceptions;
using Odin.Core.Serialization;
using Odin.Core.Storage.SQLite;
using Odin.Core.Util;
using Odin.Services.Base;
using Odin.Services.DataSubscription;
using Odin.Services.Drives;
using Odin.Services.Drives.FileSystem;
using Odin.Services.Drives.Management;
using Odin.Services.EncryptionKeyService;
using Odin.Services.Mediator.Owner;
using Odin.Services.Membership.Connections;
using Odin.Services.Peer.Encryption;
using Odin.Services.Peer.Incoming.Drive.Transfer.InboxStorage;
using Odin.Services.Peer.Outgoing.Drive;

namespace Odin.Services.Peer.Incoming.Drive.Transfer
{
    public class PeerInboxProcessor(
        TransitInboxBoxStorage transitInboxBoxStorage,
        FileSystemResolver fileSystemResolver,
        CircleNetworkService circleNetworkService,
        ILogger<PeerInboxProcessor> logger,
        PublicPrivateKeyService keyService,
        DriveManager driveManager)
        : INotificationHandler<RsaKeyRotatedNotification>
    {
        public const string ReadReceiptItemMarkedComplete = "ReadReceipt Marked As Complete";
        
        /// <summary>
        /// Processes incoming transfers by converting their transfer
        /// keys and moving files to long term storage.  Returns the number of items in the inbox
        /// </summary>
        public async Task<InboxStatus> ProcessInbox(TargetDrive targetDrive, IOdinContext odinContext, DatabaseConnection cn, int batchSize = 1)
        {
            var driveId = odinContext.PermissionsContext.GetDriveId(targetDrive);
            logger.LogDebug("Processing Inbox -> Getting Pending Items for drive {driveId} with batchSize: {batchSize}", driveId, batchSize);
            var items = await transitInboxBoxStorage.GetPendingItems(driveId, batchSize, cn);

            var status = transitInboxBoxStorage.GetPendingCount(driveId, cn);
            logger.LogDebug("Status for drive: [{targetDrive}]: popped:{popped}, total: {totalCount}, oldest:{oldest}", targetDrive.ToString(),
                status.PoppedCount, status.TotalItems,
                status.OldestItemTimestamp.milliseconds);

            PeerFileWriter writer = new PeerFileWriter(logger, fileSystemResolver, driveManager);
            logger.LogDebug("Processing Inbox -> Getting Pending Items returned: {itemCount}", items.Count);
            foreach (var inboxItem in items)
            {
                logger.LogDebug("Processing Inbox (no call to CUOWA) item with marker/popStamp [{marker}]", inboxItem.Marker);

                // await cn.CreateCommitUnitOfWorkAsync(async () =>
                // {
                var tempFile = new InternalDriveFileId()
                {
                    DriveId = inboxItem.DriveId,
                    FileId = inboxItem.FileId
                };
                
                try
                {
                    var fs = fileSystemResolver.ResolveFileSystem(inboxItem.FileSystemType);
                    
                    
                    if (inboxItem.InstructionType == TransferInstructionType.SaveFile)
                    {
                        if (inboxItem.TransferFileType == TransferFileType.EncryptedFileForFeed)
                        {
                            await ProcessFeedInboxItem(odinContext, inboxItem, writer, tempFile, fs, cn);
                        }
                        else
                        {
                            var icr = await circleNetworkService.GetIdentityConnectionRegistration(inboxItem.Sender,
                                odinContext, cn, overrideHack: true);
                            var sharedSecret =
                                icr.CreateClientAccessToken(odinContext.PermissionsContext.GetIcrKey())
                                    .SharedSecret;
                            var decryptedKeyHeader =
                                inboxItem.SharedSecretEncryptedKeyHeader.DecryptAesToKeyHeader(ref sharedSecret);

                            var handleFileMs = await Benchmark.MillisecondsAsync(async () =>
                            {
                                await writer.HandleFile(tempFile, fs, decryptedKeyHeader, inboxItem.Sender,
                                    inboxItem.TransferInstructionSet,
                                    odinContext, cn);
                            });

                            logger.LogDebug("Processing Inbox -> HandleFile Complete. Took {ms} ms", handleFileMs);
                        }
                    }
                    else if (inboxItem.InstructionType == TransferInstructionType.DeleteLinkedFile)
                    {
                        logger.LogDebug("Processing Inbox -> DeleteFile marker/popstamp:[{maker}]",
                            Utilities.BytesToHexString(inboxItem.Marker.ToByteArray()));
                        await writer.DeleteFile(fs, inboxItem, odinContext, cn);
                    }
                    else if (inboxItem.InstructionType == TransferInstructionType.ReadReceipt)
                    {
                        logger.LogDebug("Processing Inbox -> ReadReceipt marker/popstamp:[{maker}]",
                            Utilities.BytesToHexString(inboxItem.Marker.ToByteArray()));
                        
                        await writer.MarkFileAsRead(fs, inboxItem, odinContext, cn);
                        await transitInboxBoxStorage.MarkComplete(tempFile, inboxItem.Marker, cn);
                        
                        logger.LogDebug(ReadReceiptItemMarkedComplete);
                    }
                    else if (inboxItem.InstructionType == TransferInstructionType.None)
                    {
                        throw new OdinClientException("Transfer type not specified",
                            OdinClientErrorCode.TransferTypeNotSpecified);
                    }
                    else
                    {
                        await transitInboxBoxStorage.MarkComplete(tempFile, inboxItem.Marker, cn);
                        throw new OdinClientException("Invalid transfer type",
                            OdinClientErrorCode.InvalidTransferType);
                    }

                    logger.LogDebug("Processing Inbox -> MarkComplete: marker: {marker} for drive: {driveId}",
                        Utilities.BytesToHexString(inboxItem.Marker.ToByteArray()),
                        Utilities.BytesToHexString(inboxItem.DriveId.ToByteArray()));
                    await transitInboxBoxStorage.MarkComplete(tempFile, inboxItem.Marker, cn);
                }
                catch (OdinRemoteIdentityException)
                {
                    await transitInboxBoxStorage.MarkFailure(tempFile, inboxItem.Marker, cn);
                    throw;
                }
                catch (OdinFileWriteException)
                {
                    logger.LogError(
                        "File was missing for inbox item.  the inbox item will be removed.  marker/popStamp: [{marker}]",
                        Utilities.BytesToHexString(inboxItem.Marker.ToByteArray()));
                    await transitInboxBoxStorage.MarkComplete(tempFile, inboxItem.Marker, cn);
                }
                catch (Exception e)
                {
                    logger.LogError(
                        "Processing Inbox -> Marking Complete (Catch-all Exception): Failed with exception: {message}\n{stackTrace}\n file:{f}\n inbox item gtid: {gtid}",
                        e.Message, 
                        e.StackTrace,
                        tempFile,
                        inboxItem.GlobalTransitId);
                    logger.LogError(
                        "Processing Inbox -> Catch-all Exception of type [{exceptionType}]): Marking Complete PopStamp (hex): {marker} for drive (hex): {driveId}",
                        e.GetType().Name,
                        Utilities.BytesToHexString(inboxItem.Marker.ToByteArray()),
                        Utilities.BytesToHexString(inboxItem.DriveId.ToByteArray()));
                    await transitInboxBoxStorage.MarkComplete(tempFile, inboxItem.Marker, cn);
                }
                // });
            }

            var pendingCount = transitInboxBoxStorage.GetPendingCount(driveId, cn);
            logger.LogDebug("Returning: Status for drive: [{targetDrive}]: popped:{popped}, total: {totalCount}, oldest:{oldest}", targetDrive.ToString(),
                pendingCount.PoppedCount, pendingCount.TotalItems,
                pendingCount.OldestItemTimestamp.milliseconds);
            return pendingCount;
        }

        private async Task ProcessFeedInboxItem(IOdinContext odinContext, TransferInboxItem inboxItem, PeerFileWriter writer, InternalDriveFileId tempFile,
            IDriveFileSystem fs, DatabaseConnection cn)
        {
            try
            {
                logger.LogDebug("Processing Feed Inbox Item -> Handling TransferFileType.EncryptedFileForFeed");

                byte[] decryptedBytes = await keyService.EccDecryptPayload(inboxItem.EncryptedFeedPayload, cn);

                var feedPayload = OdinSystemSerializer.Deserialize<FeedItemPayload>(decryptedBytes.ToStringFromUtf8Bytes());
                var decryptedKeyHeader = KeyHeader.FromCombinedBytes(feedPayload.KeyHeaderBytes);

                var handleFileMs = await Benchmark.MillisecondsAsync(async () =>
                {
                    await writer.HandleFile(tempFile, fs, decryptedKeyHeader, inboxItem.Sender, inboxItem.TransferInstructionSet,
                        odinContext, cn);
                });

                logger.LogDebug("Processing Feed Inbox Item -> HandleFile Complete. Took {ms} ms", handleFileMs);
            }
            catch (Exception e)
            {
                logger.LogError(e, "[Experimental collaborative channel support inbox processing failed; swallowing error]");
            }
        }

        public Task Handle(RsaKeyRotatedNotification notification, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}