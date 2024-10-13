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
using Odin.Core.Storage.SQLite.IdentityDatabase;
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

        public async Task<InboxStatus> ProcessInbox(TargetDrive targetDrive, IOdinContext odinContext, IdentityDatabase db, int batchSize = 1)
        {
            int actualBatchSize = batchSize == 0 ? 1 : batchSize;
            var driveId = odinContext.PermissionsContext.GetDriveId(targetDrive);
            logger.LogDebug("Processing Inbox -> Getting Pending Items (chatty) for drive {driveId} with requested " +
                            "batchSize: {batchSize}; actualBatchSize: {actualBatchSize}", driveId,
                batchSize, actualBatchSize);

            var status = transitInboxBoxStorage.GetPendingCount(driveId, db);
            logger.LogDebug("Status for drive: [{targetDrive}]: popped:{popped}, total: {totalCount}, oldest:{oldest}", targetDrive.ToString(),
                status.PoppedCount, status.TotalItems,
                status.OldestItemTimestamp.milliseconds);

            for (int i = 0; i < actualBatchSize; i++)
            {
                var items = await transitInboxBoxStorage.GetPendingItems(driveId, 1, db);

                // if nothing comes back; exit
                var inboxItem = items?.FirstOrDefault();
                if (inboxItem == null)
                {
                    logger.LogDebug("Processing Inbox -> No inbox item");
                    return GetPendingCount(targetDrive, db, driveId);
                }

                logger.LogDebug("Processing Inbox -> Getting Pending Items returned: {itemCount}", items.Count);
                logger.LogDebug("Processing Inbox (no call to CUOWA) item with marker/popStamp [{marker}]", inboxItem.Marker);

                await ProcessInboxItem(inboxItem, odinContext);
            }

            return GetPendingCount(targetDrive, db, driveId);
        }

        /// <summary>
        /// Processes incoming transfers by converting their transfer
        /// keys and moving files to long term storage.  Returns the number of items in the inbox
        /// </summary>
        private async Task ProcessInboxItem(TransferInboxItem inboxItem, IOdinContext odinContext)
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

                        await transitInboxBoxStorage.MarkComplete(tempFile, inboxItem.Marker, tenantSystemStorage.IdentityDatabase);
                    }
                    else if (inboxItem.TransferFileType == TransferFileType.EncryptedFileForFeed)
                    {
                        await ProcessFeedInboxItem(odinContext, inboxItem, writer, tempFile, fs, tenantSystemStorage.IdentityDatabase);
                    }
                    else
                    {
                        logger.LogDebug("Processing Inbox -> HandleFile with gtid: {gtid}", inboxItem.GlobalTransitId);

<<<<<<< HEAD
                        var decryptedKeyHeader = await DecryptedKeyHeader(inboxItem.Sender, inboxItem.SharedSecretEncryptedKeyHeader, odinContext, cn);
=======
                        var icr = await circleNetworkService.GetIdentityConnectionRegistration(inboxItem.Sender, odinContext, tenantSystemStorage.IdentityDatabase, overrideHack: true);
                        var sharedSecret = icr.CreateClientAccessToken(odinContext.PermissionsContext.GetIcrKey()).SharedSecret;
                        var decryptedKeyHeader = inboxItem.SharedSecretEncryptedKeyHeader.DecryptAesToKeyHeader(ref sharedSecret);

>>>>>>> main
                        var handleFileMs = await Benchmark.MillisecondsAsync(async () =>
                        {
                            await writer.HandleFile(tempFile, fs, decryptedKeyHeader, inboxItem.Sender,
                                inboxItem.TransferInstructionSet,
                                odinContext, tenantSystemStorage.IdentityDatabase);
                        });

                        logger.LogDebug("Processing Inbox -> HandleFile Complete. gtid: {gtid} Took {ms} ms", inboxItem.GlobalTransitId, handleFileMs);
                    }
                }
                else if (inboxItem.InstructionType == TransferInstructionType.DeleteLinkedFile)
                {
                    logger.LogDebug("Processing Inbox -> DeleteFile marker/popstamp:[{maker}]",
                        Utilities.BytesToHexString(inboxItem.Marker.ToByteArray()));
                    await writer.DeleteFile(fs, inboxItem, odinContext, tenantSystemStorage.IdentityDatabase);
                }
                else if (inboxItem.InstructionType == TransferInstructionType.ReadReceipt)
                {
                    logger.LogDebug("Processing Inbox -> ReadReceipt (gtid: {gtid} gtid as hex x'{gtidHex}') marker/popstamp:[{maker}]",
                        inboxItem.GlobalTransitId,
                        Utilities.BytesToHexString(inboxItem.GlobalTransitId.ToByteArray()),
                        Utilities.BytesToHexString(inboxItem.Marker.ToByteArray()));

                    await writer.MarkFileAsRead(fs, inboxItem, odinContext, tenantSystemStorage.IdentityDatabase);
                    logger.LogDebug(ReadReceiptItemMarkedComplete);
                }
                else if (inboxItem.InstructionType is TransferInstructionType.AddReaction or TransferInstructionType.DeleteReaction)
                {
                    await HandleReaction(inboxItem, fs, odinContext, tenantSystemStorage.IdentityDatabase);
                    await transitInboxBoxStorage.MarkComplete(tempFile, inboxItem.Marker, tenantSystemStorage.IdentityDatabase);
                }
                else if (inboxItem.InstructionType == TransferInstructionType.None)
                {
                    throw new OdinClientException("Transfer type not specified", OdinClientErrorCode.TransferTypeNotSpecified);
                }
                else if (inboxItem.InstructionType == TransferInstructionType.UpdateFile)
                {
                    logger.LogDebug("Processing Inbox -> UpdateFile instruction found, ignoring and marking as failure until code supports it.");
                    await transitInboxBoxStorage.MarkFailure(tempFile, inboxItem.Marker, tenantSystemStorage.IdentityDatabase);
                }
                else
                {
                    await transitInboxBoxStorage.MarkComplete(tempFile, inboxItem.Marker, tenantSystemStorage.IdentityDatabase);
                    throw new OdinClientException("Invalid transfer type", OdinClientErrorCode.InvalidTransferType);
                }

                logger.LogDebug("Processing Inbox -> MarkComplete: marker: {marker} for drive: {driveId}",
                    Utilities.BytesToHexString(inboxItem.Marker.ToByteArray()),
                    Utilities.BytesToHexString(inboxItem.DriveId.ToByteArray()));
                await transitInboxBoxStorage.MarkComplete(tempFile, inboxItem.Marker, tenantSystemStorage.IdentityDatabase);
            }
            catch (OdinRemoteIdentityException)
            {
                await transitInboxBoxStorage.MarkFailure(tempFile, inboxItem.Marker, tenantSystemStorage.IdentityDatabase);
                throw;
            }
            catch (OdinFileWriteException ofwe)
            {
                logger.LogError(ofwe,
                    "Issue Writing a file.  Action: Marking Complete. marker/popStamp: [{marker}]",
                    Utilities.BytesToHexString(inboxItem.Marker.ToByteArray()));
                await transitInboxBoxStorage.MarkComplete(tempFile, inboxItem.Marker, tenantSystemStorage.IdentityDatabase);
            }
            catch (LockConflictException lce)
            {
                logger.LogWarning(lce,
                    "Processing Inbox -> Inbox InstructionType: {instructionType}. Action: Marking Failure; retry later: [{marker}]",
                    inboxItem.InstructionType,
                    Utilities.BytesToHexString(inboxItem.Marker.ToByteArray()));
                await transitInboxBoxStorage.MarkFailure(tempFile, inboxItem.Marker, tenantSystemStorage.IdentityDatabase);
            }
            catch (OdinAcquireLockException te)
            {
                logger.LogWarning(te,
                    "Processing Inbox -> Inbox InstructionType: {instructionType}. Action: Marking Failure; retry later: [{marker}]",
                    inboxItem.InstructionType,
                    Utilities.BytesToHexString(inboxItem.Marker.ToByteArray()));
                await transitInboxBoxStorage.MarkFailure(tempFile, inboxItem.Marker, tenantSystemStorage.IdentityDatabase);
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

                await transitInboxBoxStorage.MarkComplete(tempFile, inboxItem.Marker, tenantSystemStorage.IdentityDatabase);
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

                await transitInboxBoxStorage.MarkComplete(tempFile, inboxItem.Marker, tenantSystemStorage.IdentityDatabase);
            }
            // });
        }

<<<<<<< HEAD
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
=======
        private async Task HandleReaction(TransferInboxItem inboxItem, IDriveFileSystem fs, IOdinContext odinContext, IdentityDatabase db)
>>>>>>> main
        {
            var header = await fs.Query.GetFileByGlobalTransitId(inboxItem.DriveId, inboxItem.GlobalTransitId, odinContext, db);
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
                    await reactionContentService.AddReaction(localFile, reaction, inboxItem.Sender, odinContext, db);
                    break;

                case TransferInstructionType.DeleteReaction:
                    await reactionContentService.DeleteReaction(localFile, reaction, inboxItem.Sender, odinContext, db);
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
            IDriveFileSystem fs, IdentityDatabase db)
        {
            try
            {
                logger.LogDebug("Processing Feed Inbox Item -> Handling TransferFileType.EncryptedFileForFeed");

<<<<<<< HEAD
                byte[] decryptedBytes = await keyService.EccDecryptPayload(PublicPrivateKeyType.OfflineKey,
                    inboxItem.EncryptedFeedPayload, odinContext, cn);
=======
                byte[] decryptedBytes = await keyService.EccDecryptPayload(inboxItem.EncryptedFeedPayload, db);
>>>>>>> main

                var feedPayload = OdinSystemSerializer.Deserialize<FeedItemPayload>(decryptedBytes.ToStringFromUtf8Bytes());
                var decryptedKeyHeader = KeyHeader.FromCombinedBytes(feedPayload.KeyHeaderBytes);

                var handleFileMs = await Benchmark.MillisecondsAsync(async () =>
                {
                    await writer.HandleFile(tempFile, fs, decryptedKeyHeader, inboxItem.Sender, inboxItem.TransferInstructionSet,
<<<<<<< HEAD
                        odinContext, cn,
                        feedPayload.DriveOriginWasCollaborative);
=======
                        odinContext, db);
>>>>>>> main
                });

                logger.LogDebug("Processing Feed Inbox Item -> HandleFile Complete. Took {ms} ms", handleFileMs);
            }
            catch (Exception e)
            {
                logger.LogError(e, "[Experimental collaborative channel support inbox processing failed; swallowing error]");
            }
        }


        private InboxStatus GetPendingCount(TargetDrive targetDrive, IdentityDatabase db, Guid driveId)
        {
            var pendingCount = transitInboxBoxStorage.GetPendingCount(driveId, db);
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