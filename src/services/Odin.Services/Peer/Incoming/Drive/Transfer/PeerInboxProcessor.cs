using System;
using System.Linq;
using System.Threading.Tasks;
using Bitcoin.BitcoinUtilities;
using Microsoft.Extensions.Logging;
using Odin.Core;
using Odin.Core.Exceptions;
using Odin.Core.Identity;
using Odin.Core.Serialization;
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
        ReactionContentService reactionContentService)
    {
        public const string ReadReceiptItemMarkedComplete = "ReadReceipt Marked As Complete";

        public async Task<InboxStatus> ProcessInboxAsync(TargetDrive targetDrive, IOdinContext odinContext, int batchSize = 1)
        {
            int actualBatchSize = batchSize == 0 ? 1 : batchSize;
            var driveId = odinContext.PermissionsContext.GetDriveId(targetDrive);
            logger.LogDebug("Processing Inbox -> Getting Pending Items (chatty) for drive {driveId} with requested " +
                            "batchSize: {batchSize}; actualBatchSize: {actualBatchSize}", driveId,
                batchSize, actualBatchSize);

            var status = await transitInboxBoxStorage.GetPendingCountAsync(driveId);
            logger.LogDebug("Status for drive: [{targetDrive}]: popped:{popped}, total: {totalCount}, oldest:{oldest}",
                targetDrive.ToString(),
                status.PoppedCount, status.TotalItems,
                status.OldestItemTimestamp.milliseconds);

            for (int i = 0; i < actualBatchSize; i++)
            {
                var items = await transitInboxBoxStorage.GetPendingItemsAsync(driveId, 1);

                // if nothing comes back; exit
                var inboxItem = items?.FirstOrDefault();
                if (inboxItem == null)
                {
                    logger.LogDebug("Processing Inbox -> No inbox item");
                    return await GetPendingCountAsync(targetDrive, driveId);
                }

                logger.LogDebug("Processing Inbox -> Getting Pending Items returned: {itemCount}", items.Count);
                logger.LogDebug("Processing Inbox (no call to CUOWA) item with marker/popStamp [{marker}]", inboxItem.Marker);

                await ProcessInboxItemAsync(inboxItem, odinContext);
            }

            return await GetPendingCountAsync(targetDrive, driveId);
        }

        /// <summary>
        /// Processes incoming transfers by converting their transfer
        /// keys and moving files to long term storage.  Returns the number of items in the inbox
        /// </summary>
        private async Task ProcessInboxItemAsync(TransferInboxItem inboxItem, IOdinContext odinContext)
        {
            PeerFileWriter writer = new PeerFileWriter(logger, fileSystemResolver, driveManager);

            // await db.CreateCommitUnitOfWorkAsync(async () =>
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
                    await HandleUpdateFileAsync(inboxItem, odinContext);
                    await transitInboxBoxStorage.MarkCompleteAsync(tempFile, inboxItem.Marker);
                }
                else if (inboxItem.InstructionType == TransferInstructionType.SaveFile)
                {
                    if (inboxItem.TransferFileType == TransferFileType.CommandMessage)
                    {
                        logger.LogInformation(
                            "Found inbox item of type CommandMessage; these are now obsolete (gtid: {gtid} InstructionType:{it}); Action: Marking Complete",
                            inboxItem.GlobalTransitId, inboxItem.InstructionType);

                        await transitInboxBoxStorage.MarkCompleteAsync(tempFile, inboxItem.Marker);
                    }

                    else if (inboxItem.TransferFileType == TransferFileType.EncryptedFileForFeedViaTransit)
                    {
                        //this was a file sent over transit (fully encrypted for connected identities but targeting the feed drive)
                        await ProcessFeedItemViaTransit(inboxItem, odinContext, writer, tempFile, fs);
                    }
                    else if (inboxItem.TransferFileType == TransferFileType.EncryptedFileForFeed) //older path
                    {
                        await ProcessEccEncryptedFeedInboxItem(inboxItem, writer, tempFile, fs, odinContext);
                    }
                    else
                    {
                        await ProcessNormalFileSaveOperation(inboxItem, odinContext, writer, tempFile, fs);
                    }
                }
                else if (inboxItem.InstructionType == TransferInstructionType.DeleteLinkedFile)
                {
                    logger.LogDebug("Processing Inbox -> DeleteFile marker/popstamp:[{maker}]",
                        Utilities.BytesToHexString(inboxItem.Marker.ToByteArray()));
                    await writer.DeleteFile(fs, inboxItem, odinContext);
                }
                else if (inboxItem.InstructionType == TransferInstructionType.ReadReceipt)
                {
                    logger.LogDebug("Processing Inbox -> ReadReceipt (gtid: {gtid} gtid as hex x'{gtidHex}') marker/popstamp:[{maker}]",
                        inboxItem.GlobalTransitId,
                        Utilities.BytesToHexString(inboxItem.GlobalTransitId.ToByteArray()),
                        Utilities.BytesToHexString(inboxItem.Marker.ToByteArray()));

                    await writer.MarkFileAsRead(fs, inboxItem, odinContext);
                    logger.LogDebug(ReadReceiptItemMarkedComplete);
                }
                else if (inboxItem.InstructionType is TransferInstructionType.AddReaction or TransferInstructionType.DeleteReaction)
                {
                    await HandleReaction(inboxItem, fs, odinContext);
                    await transitInboxBoxStorage.MarkCompleteAsync(tempFile, inboxItem.Marker);
                }
                else
                {
                    await transitInboxBoxStorage.MarkCompleteAsync(tempFile, inboxItem.Marker);
                    throw new OdinClientException("Invalid transfer type or not specified", OdinClientErrorCode.InvalidTransferType);
                }

                logger.LogDebug("Processing Inbox -> MarkComplete: marker: {marker} for drive: {driveId}",
                    Utilities.BytesToHexString(inboxItem.Marker.ToByteArray()),
                    Utilities.BytesToHexString(inboxItem.DriveId.ToByteArray()));
                await transitInboxBoxStorage.MarkCompleteAsync(tempFile, inboxItem.Marker);
            }
            catch (OdinRemoteIdentityException)
            {
                await transitInboxBoxStorage.MarkFailureAsync(tempFile, inboxItem.Marker);
                throw;
            }
            catch (OdinFileWriteException ofwe)
            {
                logger.LogError(ofwe,
                    "Issue Writing a file.  Action: Marking Complete. marker/popStamp: [{marker}]",
                    Utilities.BytesToHexString(inboxItem.Marker.ToByteArray()));
                await transitInboxBoxStorage.MarkCompleteAsync(tempFile, inboxItem.Marker);
            }
            catch (OdinAcquireLockException te)
            {
                logger.LogInformation(te,
                    "Processing Inbox -> Inbox InstructionType: {instructionType}. Action: Marking Failure; retry later: [{marker}]",
                    inboxItem.InstructionType,
                    Utilities.BytesToHexString(inboxItem.Marker.ToByteArray()));
                await transitInboxBoxStorage.MarkFailureAsync(tempFile, inboxItem.Marker);
            }
            catch (OdinClientException oce)
            {
                if (oce.ErrorCode == OdinClientErrorCode.ExistingFileWithUniqueId)
                {
                    logger.LogInformation(oce,
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

                await transitInboxBoxStorage.MarkCompleteAsync(tempFile, inboxItem.Marker);
            }
            catch (OdinSecurityException securityException)
            {
                logger.LogInformation(securityException,
                    "Processing Inbox -> Security Exception: " +
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

                await transitInboxBoxStorage.MarkCompleteAsync(tempFile, inboxItem.Marker);
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

                await transitInboxBoxStorage.MarkCompleteAsync(tempFile, inboxItem.Marker);
            }
            // });
        }

        private async Task ProcessNormalFileSaveOperation(TransferInboxItem inboxItem, IOdinContext odinContext, PeerFileWriter writer,
            InternalDriveFileId tempFile, IDriveFileSystem fs)
        {
            logger.LogDebug("Processing Inbox -> HandleFile with gtid: {gtid}", inboxItem.GlobalTransitId);

            var decryptedKeyHeader = await DecryptedKeyHeaderAsync(inboxItem.Sender, inboxItem.SharedSecretEncryptedKeyHeader, odinContext);
            var handleFileMs = await Benchmark.MillisecondsAsync(async () =>
            {
                await writer.HandleFile(tempFile, fs, decryptedKeyHeader, inboxItem.Sender,
                    inboxItem.TransferInstructionSet,
                    odinContext);
            });

            logger.LogDebug("Processing Inbox -> HandleFile Complete. gtid: {gtid} Took {ms} ms", inboxItem.GlobalTransitId,
                handleFileMs);
        }

        private async Task ProcessFeedItemViaTransit(TransferInboxItem inboxItem, IOdinContext odinContext, PeerFileWriter writer,
            InternalDriveFileId tempFile, IDriveFileSystem fs)
        {
            logger.LogDebug("ProcessFeedItemViaTransit -> HandleFile with gtid: {gtid}", inboxItem.GlobalTransitId);

            var decryptedKeyHeader = await DecryptedKeyHeaderAsync(inboxItem.Sender, inboxItem.SharedSecretEncryptedKeyHeader, odinContext);
            var handleFileMs = await Benchmark.MillisecondsAsync(async () =>
            {
                await writer.HandleFile(tempFile, fs, decryptedKeyHeader, inboxItem.Sender,
                    inboxItem.TransferInstructionSet,
                    odinContext);
            });

            logger.LogDebug("ProcessFeedItemViaTransit -> HandleFile Complete. gtid: {gtid} Took {ms} ms", inboxItem.GlobalTransitId,
                handleFileMs);
        }

        private async Task HandleUpdateFileAsync(TransferInboxItem inboxItem, IOdinContext odinContext)
        {
            var writer = new PeerFileUpdateWriter(logger, fileSystemResolver, driveManager);
            var tempFile = new InternalDriveFileId()
            {
                FileId = inboxItem.FileId,
                DriveId = inboxItem.DriveId
            };

            var updateInstructionSet =
                OdinSystemSerializer.Deserialize<EncryptedRecipientFileUpdateInstructionSet>(inboxItem.Data.ToStringFromUtf8Bytes());
            var decryptedKeyHeader =
                await DecryptedKeyHeaderAsync(inboxItem.Sender, updateInstructionSet.EncryptedKeyHeader, odinContext);
            await writer.UpsertFileAsync(tempFile, decryptedKeyHeader, inboxItem.Sender, updateInstructionSet, odinContext);
        }

        private async Task HandleReaction(TransferInboxItem inboxItem, IDriveFileSystem fs, IOdinContext odinContext)
        {
            var header = await fs.Query.GetFileByGlobalTransitId(inboxItem.DriveId, inboxItem.GlobalTransitId, odinContext);
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

            var reaction = DecryptUsingSharedSecret<string>(request.Payload);

            switch (inboxItem.InstructionType)
            {
                case TransferInstructionType.AddReaction:
                    await reactionContentService.AddReactionAsync(localFile, reaction, inboxItem.Sender, odinContext);
                    break;

                case TransferInstructionType.DeleteReaction:
                    await reactionContentService.DeleteReactionAsync(localFile, reaction, inboxItem.Sender, odinContext);
                    break;
            }
        }

        private T DecryptUsingSharedSecret<T>(SharedSecretEncryptedTransitPayload payload)
        {
            //TODO: put decryption back in place
            // var t = await ResolveClientAccessToken(caller!.Value, tokenSource);
            // var sharedSecret = t.SharedSecret;
            // var encryptedBytes = Convert.FromBase64String(payload.Data);
            // var decryptedBytes = AesCbc.Decrypt(encryptedBytes, ref sharedSecret, payload.Iv);

            var decryptedBytes = Convert.FromBase64String(payload.Data);
            var json = decryptedBytes.ToStringFromUtf8Bytes();
            return OdinSystemSerializer.Deserialize<T>(json);
        }

        private async Task ProcessEccEncryptedFeedInboxItem(TransferInboxItem inboxItem, PeerFileWriter writer,
            InternalDriveFileId tempFile,
            IDriveFileSystem fs,
            IOdinContext odinContext)
        {
            try
            {
                logger.LogDebug("Processing Feed Inbox Item -> Handling TransferFileType.EncryptedFileForFeed");

                byte[] decryptedBytes = await keyService.EccDecryptPayload(inboxItem.EncryptedFeedPayload, odinContext);

                var feedPayload = OdinSystemSerializer.Deserialize<FeedItemPayload>(decryptedBytes.ToStringFromUtf8Bytes());
                var decryptedKeyHeader = KeyHeader.FromCombinedBytes(feedPayload.KeyHeaderBytes);

                var handleFileMs = await Benchmark.MillisecondsAsync(async () =>
                {
                    await writer.HandleFile(tempFile, fs, decryptedKeyHeader, inboxItem.Sender, inboxItem.TransferInstructionSet,
                        odinContext, feedPayload.DriveOriginWasCollaborative);
                });

                logger.LogDebug("Processing Feed Inbox Item -> HandleFile Complete. Took {ms} ms", handleFileMs);
            }
            catch (Exception e)
            {
                logger.LogDebug(e, "[Experimental collaborative channel support inbox processing failed; ignoring error]");
            }
        }


        private async Task<InboxStatus> GetPendingCountAsync(TargetDrive targetDrive, Guid driveId)
        {
            var pendingCount = await transitInboxBoxStorage.GetPendingCountAsync(driveId);
            logger.LogDebug("Returning: Status for drive: [{targetDrive}]: popped:{popped}, total: {totalCount}, oldest:{oldest}",
                targetDrive.ToString(),
                pendingCount.PoppedCount, pendingCount.TotalItems,
                pendingCount.OldestItemTimestamp.milliseconds);
            return pendingCount;
        }

        private async Task<KeyHeader> DecryptedKeyHeaderAsync(OdinId sender, EncryptedKeyHeader encryptedKeyHeader,
            IOdinContext odinContext)
        {
            var icr = await circleNetworkService.GetIcrAsync(sender, odinContext, overrideHack: true, tryUpgradeEncryption: true);
            var sharedSecret = icr.CreateClientAccessToken(odinContext.PermissionsContext.GetIcrKey()).SharedSecret;
            var decryptedKeyHeader = encryptedKeyHeader.DecryptAesToKeyHeader(ref sharedSecret);
            return decryptedKeyHeader;
        }
    }
}