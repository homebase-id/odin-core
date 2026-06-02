using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bitcoin.BitcoinUtilities;
using Microsoft.Extensions.Logging;
using Odin.Core;
using Odin.Core.Exceptions;
using Odin.Core.Identity;
using Odin.Core.Logging.CorrelationId;
using Odin.Core.Serialization;
using Odin.Core.Storage.Concurrency;
using Odin.Core.Util;
using Odin.Services.Base;
using Odin.Services.DataSubscription;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Storage;
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
    public abstract class WriteSecondDatabaseRowBase
    {
        public abstract Task<int> ExecuteAsync();
    }

    public class MarkInboxComplete(TransitInboxBoxStorage s, InternalDriveFileId t, Guid m) : WriteSecondDatabaseRowBase
    {
        public override async Task<int> ExecuteAsync()
        {
            return await s.MarkCompleteAsync(t, m);
        }
    }

    public enum InboxReturnTypes
    {
        HasBeenMarkedComplete = 1,
        TryAgainLater = 2,
        DeleteFromInbox = 3
    }

    public class PeerInboxProcessor(
        TransitInboxBoxStorage transitInboxBoxStorage,
        FileSystemResolver fileSystemResolver,
        CircleNetworkService circleNetworkService,
        ILogger<PeerInboxProcessor> logger,
        PublicPrivateKeyService keyService,
        IDriveManager driveManager,
        ReactionContentService reactionContentService,
        ICorrelationContext correlationContext,
        FeedWriter feedWriter,
        INodeLock nodeLock)
    {
        private static string FallbackCorrelationId => Guid.NewGuid().ToString().Remove(9, 4).Insert(9, "INBX");

        public const string ReadReceiptItemMarkedComplete = "ReadReceipt Marked As Complete";

        // Drain this box's inbox, waiting our turn for the box lock. Used by the explicit "process my
        // inbox" callers (the process-inbox endpoints and the websocket commands) and the background
        // drainer: they all want the work done, so they block rather than skip. Serialization is per
        // box (boxId == driveId today), which keeps FIFO-within-box processing intact and prevents the
        // concurrent create/delete race that orphaned files.
        public async Task<InboxStatus> ProcessInboxAsync(TargetDrive targetDrive, IOdinContext odinContext,
            int batchSize = 1, CancellationToken cancellationToken = default)
        {
            IAsyncDisposable boxLock;
            try
            {
                boxLock = await nodeLock.LockAsync(BuildBoxLockKey(targetDrive, odinContext), cancellationToken: cancellationToken);
            }
            catch (RedisLockException)
            {
                // Couldn't acquire the box lock within the timeout: another worker is already draining
                // this box. Don't fail the caller -- report the current pending state and let the holder
                // (or the background drainer) finish the work. OperationCanceledException is deliberately
                // NOT caught here: a cancelled/aborted request must propagate, not degrade to "pending".
                logger.LogDebug(
                    "Processing Inbox -> box lock busy (acquire timed out) for drive {driveId}; returning pending status",
                    targetDrive.Alias);
                return await GetPendingCountAsync(targetDrive, targetDrive.Alias);
            }

            await using (boxLock)
            {
                return await DrainInboxAsync(targetDrive, odinContext, batchSize, cancellationToken);
            }
        }

        // Best-effort drain for incidental callers (the inline drain-on-query path). If another worker
        // already holds the box lock, skip instead of blocking the request; the holder, or the
        // background drainer, will process the items. Returns the current pending state when skipped.
        public async Task<InboxStatus> TryProcessInboxAsync(TargetDrive targetDrive, IOdinContext odinContext,
            int batchSize = 1, CancellationToken cancellationToken = default)
        {
            InboxStatus status = null;
            var drained = await nodeLock.TryRunWithLockAsync(
                BuildBoxLockKey(targetDrive, odinContext),
                async () => { status = await DrainInboxAsync(targetDrive, odinContext, batchSize, cancellationToken); },
                cancellationToken: cancellationToken);

            if (!drained)
            {
                logger.LogDebug(
                    "Processing Inbox -> skipped; box lock held by another worker for drive {driveId}", targetDrive.Alias);
                return await GetPendingCountAsync(targetDrive, targetDrive.Alias);
            }

            return status;
        }

        // The lock unit is the inbox box, not the drive: boxId is what PopSpecificBoxAsync filters and
        // orders on (it equals driveId today, but the box is the real ordering unit). Tenant-qualified
        // because INodeLock is a system-wide singleton and box ids are only unique within a tenant.
        private static NodeLockKey BuildBoxLockKey(TargetDrive targetDrive, IOdinContext odinContext)
        {
            return NodeLockKey.Create("peer-inbox", odinContext.Tenant.DomainName, targetDrive.Alias.ToString());
        }

        private async Task<InboxStatus> DrainInboxAsync(TargetDrive targetDrive, IOdinContext odinContext, int batchSize = 1,
            CancellationToken cancellationToken = default)
        {
            int actualBatchSize = batchSize < 1 ? 1 : batchSize;
            var driveId = targetDrive.Alias;
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
                // Stop draining between items if the caller (e.g. an aborted request, or shutdown for
                // the background service) cancels. Items already marked complete stay done; the rest
                // remain in the inbox for the next drain.
                cancellationToken.ThrowIfCancellationRequested();

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

                var file = new InternalDriveFileId()
                {
                    DriveId = inboxItem.DriveId,
                    FileId = inboxItem.FileId
                };

                PeerFileWriter writer = new PeerFileWriter(logger, fileSystemResolver, driveManager, feedWriter);
                var markComplete = new MarkInboxComplete(transitInboxBoxStorage, file, inboxItem.Marker);
                var success = InboxReturnTypes.DeleteFromInbox;

                try
                {
                    // This function will have marked the inbox item as complete if successful
                    // Otherwise if it returns false, it's a failure
                    (success, _) = await ProcessInboxItemAsync(file, inboxItem, writer, odinContext, markComplete);

                    logger.LogDebug("Item with file ({fileId}) Processed.  success: {s}", file.FileId, success);
                }
                finally
                {
                    if (success == InboxReturnTypes.TryAgainLater)
                    {
                        int n = await transitInboxBoxStorage.MarkFailureAsync(file, inboxItem.Marker);
                        if (n != 1)
                            logger.LogError("Inbox: Unable to MarkFailureAsync for TryAgainLater.");
                    }
                    else if (success == InboxReturnTypes.DeleteFromInbox)
                    {
                        int n = await transitInboxBoxStorage.MarkCompleteAsync(file,
                            inboxItem.Marker); // markComplete removes in from the Inbox

                        if (n == 1)
                        {
                            var fs = fileSystemResolver.ResolveFileSystem(inboxItem.FileSystemType);
                            await fs.Storage.CleanupInboxTemporaryFiles(file, odinContext);
                        }
                        else
                        {
                            logger.LogError("Inbox: Unable to MarkComplete for DeleteFromInbox.");
                        }
                    }
                    else if (success == InboxReturnTypes.HasBeenMarkedComplete)
                    {
                        var fs = fileSystemResolver.ResolveFileSystem(inboxItem.FileSystemType);
                        await fs.Storage.CleanupInboxTemporaryFiles(file, odinContext);
                    }
                }
            }

            return await GetPendingCountAsync(targetDrive, driveId);
        }


        /// <summary>
        /// Processes incoming transfers by converting their transfer
        /// keys and moving files to long term storage.  Returns the number of items in the inbox
        /// success return true: The item is "complete" and should be removed from the inbox, 
        /// success return false: the item is failed and we should retry later
        /// This function should never throw an exception, only return true / false
        /// </summary>
        private async Task<(InboxReturnTypes, List<PayloadDescriptor> payloads)> ProcessInboxItemAsync(InternalDriveFileId file,
            TransferInboxItem inboxItem, PeerFileWriter writer,
            IOdinContext odinContext, WriteSecondDatabaseRowBase markComplete)
        {
            var newId = inboxItem.CorrelationId ?? FallbackCorrelationId;
            logger.LogDebug("Using correlationId from inbox item.  Old Id: {oldId} New Id: {newId}", correlationContext.Id, newId);
            correlationContext.Id = newId;
            logger.LogDebug("Begin processing Inbox item");

            try
            {
                var fs = fileSystemResolver.ResolveFileSystem(inboxItem.FileSystemType);

                if (inboxItem.InstructionType == TransferInstructionType.UpdateFile)
                {
                    var (success, payloadDescriptors) = await HandleUpdateFileAsync(file, inboxItem, odinContext, markComplete);
                    return (success ? InboxReturnTypes.HasBeenMarkedComplete : InboxReturnTypes.TryAgainLater, payloadDescriptors);
                }

                if (inboxItem.InstructionType == TransferInstructionType.SaveFile)
                {
                    if (inboxItem.TransferFileType == TransferFileType.CommandMessage)
                    {
                        logger.LogInformation(
                            "Found inbox item of type CommandMessage; these are now obsolete (gtid: {gtid} InstructionType:{it}); Action: Marking Complete",
                            inboxItem.GlobalTransitId, inboxItem.InstructionType);
                        return (InboxReturnTypes.DeleteFromInbox, []);
                    }

                    if (inboxItem.TransferFileType == TransferFileType.EncryptedFileForFeedViaTransit)
                    {
                        //this was a file sent over transit (fully encrypted for connected identities but targeting the feed drive)
                        var (success, payloadDescriptors) = await ProcessFeedItemViaTransit(inboxItem, odinContext, writer, file, fs,
                            markComplete);
                        return (success ? InboxReturnTypes.HasBeenMarkedComplete : InboxReturnTypes.TryAgainLater, payloadDescriptors);
                    }

                    if (inboxItem.TransferFileType == TransferFileType.EncryptedFileForFeed) //older path
                    {
                        var (success, payloadDescriptors) = await ProcessEccEncryptedFeedInboxItem(inboxItem, writer, file, fs,
                            odinContext, markComplete);
                        return (success ? InboxReturnTypes.HasBeenMarkedComplete : InboxReturnTypes.TryAgainLater, payloadDescriptors);
                    }

                    if (inboxItem.TransferFileType == TransferFileType.Normal)
                    {
                        var (success, payloadDescriptors) = await ProcessNormalFileSaveOperation(inboxItem, odinContext, writer, file,
                            fs, markComplete);
                        return (success ? InboxReturnTypes.HasBeenMarkedComplete : InboxReturnTypes.TryAgainLater, payloadDescriptors);
                    }

                    throw new OdinClientException("Invalid TransferFileType in SaveFile", OdinClientErrorCode.InvalidTransferType);
                }

                if (inboxItem.InstructionType == TransferInstructionType.DeleteLinkedFile)
                {
                    logger.LogDebug("[DeleteFlow] PeerInboxProcessor -> DeleteFile begin sender:{sender} gtid:{gtid} driveId:{driveId} marker/popstamp:[{maker}]",
                        inboxItem.Sender,
                        inboxItem.GlobalTransitId,
                        inboxItem.DriveId,
                        Utilities.BytesToHexString(inboxItem.Marker.ToByteArray()));
                    var success = await writer.DeleteFile(fs, inboxItem, odinContext, markComplete);
                    logger.LogDebug("[DeleteFlow] PeerInboxProcessor -> DeleteFile result success:{success} gtid:{gtid} driveId:{driveId}",
                        success, inboxItem.GlobalTransitId, inboxItem.DriveId);
                    return (success ? InboxReturnTypes.HasBeenMarkedComplete : InboxReturnTypes.TryAgainLater, []);
                }

                if (inboxItem.InstructionType == TransferInstructionType.ReadReceipt)
                {
                    logger.LogDebug("Processing Inbox -> ReadReceipt (gtid: {gtid} gtid as hex x'{gtidHex}') marker/popstamp:[{maker}] " +
                                    "InboxAdded Time(ms) {added}",
                        inboxItem.GlobalTransitId,
                        Utilities.BytesToHexString(inboxItem.GlobalTransitId.ToByteArray()),
                        Utilities.BytesToHexString(inboxItem.Marker.ToByteArray()),
                        inboxItem.AddedTimestamp.milliseconds);

                    var success = await writer.MarkFileAsRead(fs, inboxItem, odinContext, markComplete);

                    if (success)
                        logger.LogDebug(ReadReceiptItemMarkedComplete); // Used in a TEST

                    return (success ? InboxReturnTypes.HasBeenMarkedComplete : InboxReturnTypes.TryAgainLater, []);
                }

                if (inboxItem.InstructionType is TransferInstructionType.AddReaction or TransferInstructionType.DeleteReaction)
                {
                    var success = await HandleReaction(inboxItem, fs, odinContext, markComplete);
                    return (success ? InboxReturnTypes.HasBeenMarkedComplete : InboxReturnTypes.TryAgainLater, []);
                }

                throw new OdinClientException("Invalid transfer type or not specified", OdinClientErrorCode.InvalidTransferType);
            }
            catch (OdinRemoteIdentityException ex)
            {
                logger.LogError(ex, "Remote identity exception.");
                return (InboxReturnTypes.TryAgainLater, []);
            }
            catch (OdinFileWriteException ofwe)
            {
                logger.LogError(ofwe,
                    "Issue Writing a file.  Action: Marking Complete. marker/popStamp: [{marker}]",
                    Utilities.BytesToHexString(inboxItem.Marker.ToByteArray()));
                return (InboxReturnTypes.DeleteFromInbox, []);
            }
            catch (OdinAcquireLockException te)
            {
                logger.LogInformation(te,
                    "Processing Inbox -> Inbox InstructionType: {instructionType}. Action: Marking Failure; retry later: [{marker}]",
                    inboxItem.InstructionType,
                    Utilities.BytesToHexString(inboxItem.Marker.ToByteArray()));
                return (InboxReturnTypes.TryAgainLater, []); // Mark as failure
            }
            catch (OdinClientException oce)
            {
                if (oce.ErrorCode == OdinClientErrorCode.ExistingFileWithUniqueId)
                {
                    logger.LogInformation(oce,
                        "Processing Inbox -> UniqueId Conflict: " +
                        "\nSender: {sender}. " +
                        "\nInbox InstructionType: {instructionType}. " +
                        "\nFile:{f}. " +
                        "\nInbox item gtid: {gtid} (gtid as hex x'{gtidHex}'). " +
                        "\nPopStamp (hex): {marker} for drive (hex): {driveId}  " +
                        "\nAction: Marking Complete",
                        inboxItem.Sender,
                        inboxItem.InstructionType,
                        file,
                        inboxItem.GlobalTransitId,
                        Convert.ToHexString(inboxItem.GlobalTransitId.ToByteArray()),
                        Utilities.BytesToHexString(inboxItem.Marker.ToByteArray()),
                        Utilities.BytesToHexString(inboxItem.DriveId.ToByteArray()));
                }

                return (InboxReturnTypes.DeleteFromInbox, []);
            }
            catch (OdinSecurityException securityException)
            {
                logger.LogInformation(securityException,
                    "Processing Inbox -> Security Exception: " +
                    "\nSender: {sender}. " +
                    "\nInbox InstructionType: {instructionType}. " +
                    "\nFile:{f}. " +
                    "\nInbox item gtid: {gtid} (gtid as hex x'{gtidHex}'). " +
                    "\nPopStamp (hex): {marker} for drive (hex): {driveId}  " +
                    "\nAction: Marking Complete",
                    inboxItem.Sender,
                    inboxItem.InstructionType,
                    file,
                    inboxItem.GlobalTransitId,
                    Convert.ToHexString(inboxItem.GlobalTransitId.ToByteArray()),
                    Utilities.BytesToHexString(inboxItem.Marker.ToByteArray()),
                    Utilities.BytesToHexString(inboxItem.DriveId.ToByteArray()));
                return (InboxReturnTypes.DeleteFromInbox, []);
            }
            catch (OperationCanceledException)
            {
                // Cancellation (aborted request or shutdown) is not a processing failure: keep the item
                // for a later drain rather than dropping it (the catch-all below marks it complete), and
                // let the cancellation stop the drain at the next loop iteration's cancellation check.
                logger.LogDebug(
                    "Processing Inbox -> cancelled while processing item; will retry later. gtid: {gtid}",
                    inboxItem.GlobalTransitId);
                return (InboxReturnTypes.TryAgainLater, []);
            }
            catch (Exception e)
            {
                logger.LogError(e,
                    "Processing Inbox -> Inbox InstructionType: {instructionType}. " +
                    "Catch-all Exception: Failed with Temp File:{f}. " +
                    "FileId has timestamp: {ft} (is this before the inbox purge)? " +
                    "Inbox item gtid: {gtid} (gtid as hex x'{gtidHex}'). " +
                    "PopStamp (hex): {marker} for drive (hex): {driveId}  Action: Marking Complete",
                    inboxItem.InstructionType,
                    file,
                    SequentialGuid.ToUnixTimeUtc(inboxItem.FileId).ToDateTime(),
                    inboxItem.GlobalTransitId,
                    Convert.ToHexString(inboxItem.GlobalTransitId.ToByteArray()),
                    Utilities.BytesToHexString(inboxItem.Marker.ToByteArray()),
                    Utilities.BytesToHexString(inboxItem.DriveId.ToByteArray()));
                return
                    (InboxReturnTypes.DeleteFromInbox,
                        []); // TODD - SHOULD PROBABLY RETURN TryAgainLater - BUT NOT UNTIL WE HAVE A RETRY COUNT ON THE INBOX
            }
        }

        private async Task<(bool success, List<PayloadDescriptor> payloads)> ProcessNormalFileSaveOperation(TransferInboxItem inboxItem,
            IOdinContext odinContext,
            PeerFileWriter writer,
            InternalDriveFileId file, IDriveFileSystem fs, WriteSecondDatabaseRowBase markComplete)
        {
            logger.LogDebug("Processing Inbox -> HandleFile with gtid: {gtid}", inboxItem.GlobalTransitId);
            var success = false;
            List<PayloadDescriptor> payloads = [];
            var decryptedKeyHeader = await DecryptedKeyHeaderAsync(inboxItem.Sender, inboxItem.SharedSecretEncryptedKeyHeader, odinContext);
            var drive = await driveManager.GetDriveAsync(file.DriveId);
            var handleFileMs = await Benchmark.MillisecondsAsync(async () =>
            {
                (success, payloads) = await writer.HandleFile(file, fs, decryptedKeyHeader, inboxItem.Sender,
                    inboxItem.TransferInstructionSet,
                    odinContext, sourceFolderPath: drive.GetDriveInboxPath(), markComplete: markComplete);
            });

            logger.LogDebug("Processing Inbox -> HandleFile Complete. gtid: {gtid} Took {ms} ms", inboxItem.GlobalTransitId,
                handleFileMs);
            return (success, payloads);
        }

        private async Task<(bool success, List<PayloadDescriptor> payloads)> ProcessFeedItemViaTransit(TransferInboxItem inboxItem,
            IOdinContext odinContext, PeerFileWriter writer,
            InternalDriveFileId file, IDriveFileSystem fs, WriteSecondDatabaseRowBase markComplete)
        {
            logger.LogDebug("ProcessFeedItemViaTransit -> HandleFile with gtid: {gtid}", inboxItem.GlobalTransitId);

            bool success = false;
            List<PayloadDescriptor> payloads = [];
            var decryptedKeyHeader = await DecryptedKeyHeaderAsync(inboxItem.Sender, inboxItem.SharedSecretEncryptedKeyHeader, odinContext);
            var drive = await driveManager.GetDriveAsync(file.DriveId);
            var handleFileMs = await Benchmark.MillisecondsAsync(async () =>
            {
                (success, payloads) = await writer.HandleFile(file, fs, decryptedKeyHeader, inboxItem.Sender,
                    inboxItem.TransferInstructionSet,
                    odinContext, sourceFolderPath: drive.GetDriveInboxPath(), markComplete: markComplete);
            });

            logger.LogDebug("ProcessFeedItemViaTransit -> HandleFile Complete. gtid: {gtid} Took {ms} ms", inboxItem.GlobalTransitId,
                handleFileMs);
            return (success, payloads);
        }

        private async Task<(bool success, List<PayloadDescriptor> payloadDescriptors)> HandleUpdateFileAsync(InternalDriveFileId file,
            TransferInboxItem inboxItem, IOdinContext odinContext, WriteSecondDatabaseRowBase markComplete)
        {
            var writer = new PeerFileUpdateWriter(logger, fileSystemResolver, driveManager);

            var updateInstructionSet = OdinSystemSerializer.Deserialize<EncryptedRecipientFileUpdateInstructionSet>(
                inboxItem.Data.ToStringFromUtf8Bytes());

            var decryptedKeyHeader = await DecryptedKeyHeaderAsync(
                inboxItem.Sender, updateInstructionSet.EncryptedKeyHeader, odinContext);

            logger.LogDebug("PeerFileUpdateWriter called. Sender: {sender} FileId: {file}", inboxItem.Sender, inboxItem.FileId);
            var drive = await driveManager.GetDriveAsync(file.DriveId);
            return await writer.UpsertFileAsync(file, decryptedKeyHeader, inboxItem.Sender, updateInstructionSet, odinContext,
                markComplete, sourceFolderPath: drive.GetDriveInboxPath());
        }

        private async Task<bool> HandleReaction(TransferInboxItem inboxItem, IDriveFileSystem fs, IOdinContext odinContext,
            WriteSecondDatabaseRowBase markComplete)
        {
            var header = await fs.Query.GetFileByGlobalTransitId(inboxItem.DriveId, inboxItem.GlobalTransitId, odinContext);
            if (header == null)
            {
                throw new OdinClientException("HandleReaction -> No file found by GlobalTransitId", OdinClientErrorCode.InvalidFile);
            }

            var request = OdinSystemSerializer.Deserialize<RemoteReactionRequestRedux>(inboxItem.Data.ToStringFromUtf8Bytes());
            var localFile = new InternalDriveFileId
            {
                FileId = header.FileId,
                DriveId = inboxItem.DriveId
            };

            string reaction = DecryptUsingSharedSecret<string>(request.Payload);

            switch (inboxItem.InstructionType)
            {
                case TransferInstructionType.AddReaction:
                    return await reactionContentService.AddReactionAsync(localFile, reaction, inboxItem.Sender, odinContext, markComplete);

                case TransferInstructionType.DeleteReaction:
                    return await reactionContentService.DeleteReactionAsync(localFile, reaction, inboxItem.Sender, odinContext,
                        markComplete);
                default:
                    throw new OdinClientException("HandleReaction -> Invalid instruction type", OdinClientErrorCode.InvalidTransferType);
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

        private async Task<(bool success, List<PayloadDescriptor> payloads)> ProcessEccEncryptedFeedInboxItem(TransferInboxItem inboxItem,
            PeerFileWriter writer,
            InternalDriveFileId file,
            IDriveFileSystem fs,
            IOdinContext odinContext,
            WriteSecondDatabaseRowBase markComplete)
        {
            bool success = false;
            List<PayloadDescriptor> payloads = [];
            try
            {
                logger.LogDebug("Processing Feed Inbox Item -> Handling TransferFileType.EncryptedFileForFeed");

                byte[] decryptedBytes = await keyService.EccDecryptPayload(inboxItem.EncryptedFeedPayload, odinContext);

                var feedPayload = OdinSystemSerializer.Deserialize<FeedItemPayload>(decryptedBytes.ToStringFromUtf8Bytes());
                var decryptedKeyHeader = KeyHeader.FromCombinedBytes(feedPayload.KeyHeaderBytes);

                var drive = await driveManager.GetDriveAsync(file.DriveId);
                var handleFileMs = await Benchmark.MillisecondsAsync(async () =>
                {
                    (success, payloads) = await writer.HandleFile(file, fs, decryptedKeyHeader, inboxItem.Sender,
                        inboxItem.TransferInstructionSet,
                        odinContext, feedPayload.DriveOriginWasCollaborative, sourceFolderPath: drive.GetDriveInboxPath(), markComplete);
                });

                logger.LogDebug("Processing Feed Inbox Item -> HandleFile Complete. Took {ms} ms", handleFileMs);
            }
            catch (Exception e)
            {
                logger.LogDebug(e, "[Experimental collaborative channel support inbox processing failed; ignoring error]");
            }

            return (success, payloads);
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

        // private async Task AssertMetadataTempFileExists(
        //     TempFile tempFile,
        //     IDriveFileSystem fs,
        //     IOdinContext odinContext)
        // {
        //     var exists = await fs.Storage.TempFileExists(tempFile, MultipartHostTransferParts.Metadata.ToString().ToLower(), odinContext);
        //     if (!exists)
        //     {
        //         throw new OdinSystemException($"Metadata for tempFile {tempFile.ToString()} does not exist");
        //     }
        // }
    }
}