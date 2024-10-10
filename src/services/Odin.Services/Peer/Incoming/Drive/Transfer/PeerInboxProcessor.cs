using System;
using System.Linq;
using System.Threading.Tasks;
using Bitcoin.BitcoinUtilities;
using Microsoft.Extensions.Logging;
using Odin.Core;
using Odin.Core.Exceptions;
using Odin.Core.Identity;
using Odin.Core.Serialization;
using Odin.Core.Storage.SQLite;
using Odin.Core.Util;
using Odin.Services.Base;
using Odin.Services.DataSubscription;
using Odin.Services.Drives;
using Odin.Services.Drives.FileSystem;
using Odin.Services.Drives.Management;
using Odin.Services.Drives.Reactions;
using Odin.Services.EncryptionKeyService;
using Odin.Services.Membership.Connections;
using Odin.Services.Peer.Encryption;
using Odin.Services.Peer.Incoming.Drive.Transfer.FileUpdate;
using Odin.Services.Peer.Incoming.Drive.Transfer.InboxStorage;
using Odin.Services.Peer.Outgoing.Drive;
using Odin.Services.Peer.Outgoing.Drive.Reactions;

namespace Odin.Services.Peer.Incoming.Drive.Transfer
{
    public class PeerInboxProcessor(
        TransitInboxBoxStorage transitInboxBoxStorage,
        FileSystemResolver fileSystemResolver,
        CircleNetworkService circleNetworkService,
        ILogger<PeerInboxProcessor> logger,
        PublicPrivateKeyService keyService,
        DriveManager driveManager,
        TenantSystemStorage tenantSystemStorage,
        ReactionContentService reactionContentService)
    {
        public const string ReadReceiptItemMarkedComplete = "ReadReceipt Marked As Complete";

        public async Task<InboxStatus> ProcessInbox(TargetDrive targetDrive, IOdinContext odinContext, DatabaseConnection cn, int batchSize = 1)
        {
            int actualBatchSize = batchSize == 0 ? 1 : batchSize;
            var driveId = odinContext.PermissionsContext.GetDriveId(targetDrive);
            logger.LogDebug("Processing Inbox -> Getting Pending Items (chatty) for drive {driveId} with requested " +
                            "batchSize: {batchSize}; actualBatchSize: {actualBatchSize}", driveId,
                batchSize, actualBatchSize);

            var status = transitInboxBoxStorage.GetPendingCount(driveId, cn);
            logger.LogDebug("Status for drive: [{targetDrive}]: popped:{popped}, total: {totalCount}, oldest:{oldest}", targetDrive.ToString(),
                status.PoppedCount, status.TotalItems,
                status.OldestItemTimestamp.milliseconds);

            for (int i = 0; i < actualBatchSize; i++)
            {
                var items = await transitInboxBoxStorage.GetPendingItems(driveId, 1, cn);

                // if nothing comes back; exit
                var inboxItem = items?.FirstOrDefault();
                if (inboxItem == null)
                {
                    logger.LogDebug("Processing Inbox -> No inbox item");
                    return GetPendingCount(targetDrive, cn, driveId);
                }

                logger.LogDebug("Processing Inbox -> Getting Pending Items returned: {itemCount}", items.Count);
                logger.LogDebug("Processing Inbox (no call to CUOWA) item with marker/popStamp [{marker}]", inboxItem.Marker);

                await ProcessInboxItem(inboxItem, odinContext);
            }

            return GetPendingCount(targetDrive, cn, driveId);
        }

        /// <summary>
        /// Processes incoming transfers by converting their transfer
        /// keys and moving files to long term storage.  Returns the number of items in the inbox
        /// </summary>
        private async Task ProcessInboxItem(TransferInboxItem inboxItem, IOdinContext odinContext)
        {
            using var cn = tenantSystemStorage.CreateConnection();
            PeerFileWriter writer = new PeerFileWriter(logger, fileSystemResolver, driveManager);

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
                if (inboxItem.InstructionType == TransferInstructionType.UpdateFile)
                {
                    await HandleUpdateFile(inboxItem, odinContext, cn);
                    await transitInboxBoxStorage.MarkComplete(tempFile, inboxItem.Marker, cn);
                }
                else if (inboxItem.InstructionType == TransferInstructionType.SaveFile)
                {
                    if (inboxItem.TransferFileType == TransferFileType.CommandMessage)
                    {
                        logger.LogInformation(
                            "Found inbox item of type CommandMessage; these are now obsolete (gtid: {gtid} InstructionType:{it}); Action: Marking Complete",
                            inboxItem.GlobalTransitId, inboxItem.InstructionType);

                        await transitInboxBoxStorage.MarkComplete(tempFile, inboxItem.Marker, cn);
                    }
                    else if (inboxItem.TransferFileType == TransferFileType.EncryptedFileForFeed)
                    {
                        await ProcessFeedInboxItem(odinContext, inboxItem, writer, tempFile, fs, cn);
                    }
                    else
                    {
                        logger.LogDebug("Processing Inbox -> HandleFile with gtid: {gtid}", inboxItem.GlobalTransitId);

                        var decryptedKeyHeader = await DecryptedKeyHeader(inboxItem.Sender, inboxItem.SharedSecretEncryptedKeyHeader, odinContext, cn);
                        var handleFileMs = await Benchmark.MillisecondsAsync(async () =>
                        {
                            await writer.HandleFile(tempFile, fs, decryptedKeyHeader, inboxItem.Sender,
                                inboxItem.TransferInstructionSet,
                                odinContext, cn);
                        });

                        logger.LogDebug("Processing Inbox -> HandleFile Complete. gtid: {gtid} Took {ms} ms", inboxItem.GlobalTransitId, handleFileMs);
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
                    logger.LogDebug("Processing Inbox -> ReadReceipt (gtid: {gtid} gtid as hex x'{gtidHex}') marker/popstamp:[{maker}]",
                        inboxItem.GlobalTransitId,
                        Utilities.BytesToHexString(inboxItem.GlobalTransitId.ToByteArray()),
                        Utilities.BytesToHexString(inboxItem.Marker.ToByteArray()));

                    await writer.MarkFileAsRead(fs, inboxItem, odinContext, cn);
                    logger.LogDebug(ReadReceiptItemMarkedComplete);
                }
                else if (inboxItem.InstructionType is TransferInstructionType.AddReaction or TransferInstructionType.DeleteReaction)
                {
                    await HandleReaction(inboxItem, fs, odinContext, cn);
                    await transitInboxBoxStorage.MarkComplete(tempFile, inboxItem.Marker, cn);
                }

                else if (inboxItem.InstructionType == TransferInstructionType.None)
                {
                    throw new OdinClientException("Transfer type not specified", OdinClientErrorCode.TransferTypeNotSpecified);
                }
                else
                {
                    await transitInboxBoxStorage.MarkComplete(tempFile, inboxItem.Marker, cn);
                    throw new OdinClientException("Invalid transfer type", OdinClientErrorCode.InvalidTransferType);
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
            catch (OdinFileWriteException ofwe)
            {
                logger.LogError(ofwe,
                    "Issue Writing a file.  Action: Marking Complete. marker/popStamp: [{marker}]",
                    Utilities.BytesToHexString(inboxItem.Marker.ToByteArray()));
                await transitInboxBoxStorage.MarkComplete(tempFile, inboxItem.Marker, cn);
            }
            catch (LockConflictException lce)
            {
                logger.LogWarning(lce,
                    "Processing Inbox -> Inbox InstructionType: {instructionType}. Action: Marking Failure; retry later: [{marker}]",
                    inboxItem.InstructionType,
                    Utilities.BytesToHexString(inboxItem.Marker.ToByteArray()));
                await transitInboxBoxStorage.MarkFailure(tempFile, inboxItem.Marker, cn);
            }
            catch (OdinAcquireLockException te)
            {
                logger.LogWarning(te,
                    "Processing Inbox -> Inbox InstructionType: {instructionType}. Action: Marking Failure; retry later: [{marker}]",
                    inboxItem.InstructionType,
                    Utilities.BytesToHexString(inboxItem.Marker.ToByteArray()));
                await transitInboxBoxStorage.MarkFailure(tempFile, inboxItem.Marker, cn);
            }
            catch (OdinClientException oce)
            {
                if (oce.ErrorCode == OdinClientErrorCode.ExistingFileWithUniqueId)
                {
                    logger.LogError(oce,
                        "Processing Inbox -> UniqueId Conflict: " +
                        "\nSender: {sender}. " +
                        "\nInbox InstructionType: {instructionType}. " +
                        "\nTemp File:{f}. " +
                        "\nInbox item gtid: {gtid} (gtid as hex x'{gtidHex}'). " +
                        "\nPopStamp (hex): {marker} for drive (hex): {driveId}  " +
                        "\nAction: Marking Complete",
                        inboxItem.Sender,
                        inboxItem.InstructionType,
                        tempFile,
                        inboxItem.GlobalTransitId,
                        Convert.ToHexString(inboxItem.GlobalTransitId.ToByteArray()),
                        Utilities.BytesToHexString(inboxItem.Marker.ToByteArray()),
                        Utilities.BytesToHexString(inboxItem.DriveId.ToByteArray()));
                }

                await transitInboxBoxStorage.MarkComplete(tempFile, inboxItem.Marker, cn);
            }
            catch (OdinSecurityException securityException)
            {
                logger.LogWarning(securityException,
                    "Processing Inbox -> Inbox InstructionType: {instructionType}. " +
                    "OdinSecurityException: Failed with Temp File:{f}. " +
                    "Inbox item gtid: {gtid} (gtid as hex x'{gtidHex}'). " +
                    "PopStamp (hex): {marker} for drive (hex): {driveId}  Action: Marking Complete",
                    inboxItem.InstructionType,
                    tempFile,
                    inboxItem.GlobalTransitId,
                    Convert.ToHexString(inboxItem.GlobalTransitId.ToByteArray()),
                    Utilities.BytesToHexString(inboxItem.Marker.ToByteArray()),
                    Utilities.BytesToHexString(inboxItem.DriveId.ToByteArray()));

                await transitInboxBoxStorage.MarkComplete(tempFile, inboxItem.Marker, cn);
            }
            catch (Exception e)
            {
                logger.LogError(e,
                    "Processing Inbox -> Inbox InstructionType: {instructionType}. " +
                    "Catch-all Exception: Failed with Temp File:{f}. " +
                    "Inbox item gtid: {gtid} (gtid as hex x'{gtidHex}'). " +
                    "PopStamp (hex): {marker} for drive (hex): {driveId}  Action: Marking Complete",
                    inboxItem.InstructionType,
                    tempFile,
                    inboxItem.GlobalTransitId,
                    Convert.ToHexString(inboxItem.GlobalTransitId.ToByteArray()),
                    Utilities.BytesToHexString(inboxItem.Marker.ToByteArray()),
                    Utilities.BytesToHexString(inboxItem.DriveId.ToByteArray()));

                await transitInboxBoxStorage.MarkComplete(tempFile, inboxItem.Marker, cn);
            }
            // });
        }

        private async Task HandleUpdateFile(TransferInboxItem inboxItem, IOdinContext odinContext, DatabaseConnection cn)
        {
            var writer = new PeerFileUpdateWriter(logger, fileSystemResolver, driveManager);
            var tempFile = new InternalDriveFileId()
            {
                FileId = inboxItem.FileId,
                DriveId = inboxItem.DriveId
            };

            var updateInstructionSet = OdinSystemSerializer.Deserialize<EncryptedRecipientFileUpdateInstructionSet>(inboxItem.Data.ToStringFromUtf8Bytes());
            var decryptedKeyHeader = await DecryptedKeyHeader(inboxItem.Sender, updateInstructionSet.EncryptedKeyHeaderIvOnly, odinContext, cn);
            await writer.UpdateFile(tempFile, decryptedKeyHeader, inboxItem.Sender, updateInstructionSet, odinContext, cn);
        }

        private async Task HandleReaction(TransferInboxItem inboxItem, IDriveFileSystem fs, IOdinContext odinContext, DatabaseConnection connection)
        {
            var header = await fs.Query.GetFileByGlobalTransitId(inboxItem.DriveId, inboxItem.GlobalTransitId, odinContext, connection);
            if (null == header)
            {
                throw new OdinClientException("HandleReaction -> No file found by GlobalTransitId", OdinClientErrorCode.InvalidFile);
            }

            var request = OdinSystemSerializer.Deserialize<RemoteReactionRequestRedux>(inboxItem.Data.ToStringFromUtf8Bytes());

            var localFile = new InternalDriveFileId()
            {
                FileId = header.FileId,
                DriveId = inboxItem.DriveId
            };

            var reaction = await DecryptUsingSharedSecret<string>(request.Payload);

            switch (inboxItem.InstructionType)
            {
                case TransferInstructionType.AddReaction:
                    await reactionContentService.AddReaction(localFile, reaction, inboxItem.Sender, odinContext, connection);
                    break;

                case TransferInstructionType.DeleteReaction:
                    await reactionContentService.DeleteReaction(localFile, reaction, inboxItem.Sender, odinContext, connection);
                    break;
            }
        }

        private async Task<T> DecryptUsingSharedSecret<T>(SharedSecretEncryptedTransitPayload payload)
        {
            //TODO: put decryption back in place
            // var t = await ResolveClientAccessToken(caller!.Value, tokenSource);
            // var sharedSecret = t.SharedSecret;
            // var encryptedBytes = Convert.FromBase64String(payload.Data);
            // var decryptedBytes = AesCbc.Decrypt(encryptedBytes, ref sharedSecret, payload.Iv);

            var decryptedBytes = Convert.FromBase64String(payload.Data);
            var json = decryptedBytes.ToStringFromUtf8Bytes();
            return await Task.FromResult(OdinSystemSerializer.Deserialize<T>(json));
        }

        private async Task ProcessFeedInboxItem(IOdinContext odinContext, TransferInboxItem inboxItem, PeerFileWriter writer, InternalDriveFileId tempFile,
            IDriveFileSystem fs, DatabaseConnection cn)
        {
            try
            {
                logger.LogDebug("Processing Feed Inbox Item -> Handling TransferFileType.EncryptedFileForFeed");

                byte[] decryptedBytes = await keyService.EccDecryptPayload(PublicPrivateKeyType.OfflineKey,
                    inboxItem.EncryptedFeedPayload, odinContext, cn);

                var feedPayload = OdinSystemSerializer.Deserialize<FeedItemPayload>(decryptedBytes.ToStringFromUtf8Bytes());
                var decryptedKeyHeader = KeyHeader.FromCombinedBytes(feedPayload.KeyHeaderBytes);

                var handleFileMs = await Benchmark.MillisecondsAsync(async () =>
                {
                    await writer.HandleFile(tempFile, fs, decryptedKeyHeader, inboxItem.Sender, inboxItem.TransferInstructionSet,
                        odinContext, cn,
                        feedPayload.DriveOriginWasCollaborative);
                });

                logger.LogDebug("Processing Feed Inbox Item -> HandleFile Complete. Took {ms} ms", handleFileMs);
            }
            catch (Exception e)
            {
                logger.LogError(e, "[Experimental collaborative channel support inbox processing failed; swallowing error]");
            }
        }


        private InboxStatus GetPendingCount(TargetDrive targetDrive, DatabaseConnection cn, Guid driveId)
        {
            var pendingCount = transitInboxBoxStorage.GetPendingCount(driveId, cn);
            logger.LogDebug("Returning: Status for drive: [{targetDrive}]: popped:{popped}, total: {totalCount}, oldest:{oldest}", targetDrive.ToString(),
                pendingCount.PoppedCount, pendingCount.TotalItems,
                pendingCount.OldestItemTimestamp.milliseconds);
            return pendingCount;
        }

        private async Task<KeyHeader> DecryptedKeyHeader(OdinId sender, EncryptedKeyHeader encryptedKeyHeader, IOdinContext odinContext, DatabaseConnection cn)
        {
            var icr = await circleNetworkService.GetIcr(sender, odinContext, cn, overrideHack: true);
            var sharedSecret = icr.CreateClientAccessToken(odinContext.PermissionsContext.GetIcrKey()).SharedSecret;
            var decryptedKeyHeader = encryptedKeyHeader.DecryptAesToKeyHeader(ref sharedSecret);
            return decryptedKeyHeader;
        }
    }
}