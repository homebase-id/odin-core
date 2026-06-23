using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using MediatR;
using Microsoft.Extensions.Logging;
using Odin.Core;
using Odin.Core.Exceptions;
using Odin.Core.Serialization;
using Odin.Core.Storage;
using Odin.Core.Time;
using Odin.Services.AppNotifications.Push;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.FileSystem;
using Odin.Services.Drives.FileSystem.Base;
using Odin.Services.Drives.Management;
using Odin.Services.Mediator;
using Odin.Services.Peer.Encryption;
using Odin.Services.Peer.Incoming.Drive.Transfer.InboxStorage;
using Odin.Services.Peer.Outgoing.Drive;

namespace Odin.Services.Peer.Incoming.Drive.Transfer.FileUpdate
{
    public class PeerDriveIncomingFileUpdateService(
        ILogger<PeerDriveIncomingFileUpdateService> logger,
        IDriveManager driveManager,
        IDriveFileSystem fileSystem,
        IMediator mediator,
        FileSystemResolver fileSystemResolver,
        PushNotificationService pushNotificationService,
        TransitInboxBoxStorage transitInboxBoxStorage)
    {
        private EncryptedRecipientFileUpdateInstructionSet _updateInstructionSet;
        private InternalDriveFileId _file;
        private bool _isDirectWrite;

        private readonly Dictionary<string, List<string>> _uploadedKeys = new(StringComparer.InvariantCultureIgnoreCase);

        public async Task InitializeIncomingTransfer(EncryptedRecipientFileUpdateInstructionSet transferInstructionSet,
            FileMetadata fileMetadata,
            IOdinContext odinContext)
        {
            var driveId = transferInstructionSet.Request.File.TargetDrive.Alias;
            _isDirectWrite = await CanDirectWriteFile(driveId, fileMetadata, transferInstructionSet.FileSystemType, odinContext);

            // Notice here: we always create a new fileId when receiving a new file.
            _file = await fileSystem.Storage.CreateInternalFileId(driveId, odinContext);

            _updateInstructionSet = transferInstructionSet;
            await WriteInstructionsAndMetadataToDisk(fileMetadata, odinContext);
        }

        public async Task AcceptPayload(string key, string fileExtension, Stream data, IOdinContext odinContext)
        {
            _uploadedKeys.TryAdd(key, new List<string>());
            if (_isDirectWrite)
                await fileSystem.Storage.WriteUploadStream(_file, fileExtension, data, odinContext);
            else
                // Inbox-routed: stream straight to long-term under the incoming fileId (no inbox folder).
                await fileSystem.Storage.WritePayloadDirectlyToLongTerm(_file, fileExtension, data, odinContext);
        }

        public async Task AcceptThumbnail(string payloadKey, string thumbnailKey, string fileExtension, Stream data,
            IOdinContext odinContext)
        {
            if (!_uploadedKeys.TryGetValue(payloadKey, out var thumbnailKeys))
            {
                thumbnailKeys = new List<string>();
                _uploadedKeys.Add(payloadKey, thumbnailKeys);
            }

            thumbnailKeys.Add(thumbnailKey);
            _uploadedKeys[payloadKey] = thumbnailKeys;

            if (_isDirectWrite)
                await fileSystem.Storage.WriteUploadStream(_file, fileExtension, data, odinContext);
            else
                // Inbox-routed: stream straight to long-term under the incoming fileId (no inbox folder).
                await fileSystem.Storage.WritePayloadDirectlyToLongTerm(_file, fileExtension, data, odinContext);
        }

        public async Task<PeerTransferResponse> FinalizeTransfer(FileMetadata fileMetadata, IOdinContext odinContext)
        {
            // if there are payloads in the descriptor, be sure we got it all
            if (fileMetadata.Payloads?.Any() ?? false)
            {
                foreach (var expectedPayload in fileMetadata.Payloads)
                {
                    var hasPayload = _uploadedKeys.TryGetValue(expectedPayload.Key, out var thumbnailKeys);
                    if (!hasPayload)
                    {
                        throw new OdinClientException("Not all payloads received");
                    }

                    foreach (var expectedThumbnail in expectedPayload.Thumbnails)
                    {
                        var thumbnailKey = expectedThumbnail.CreateTransitKey(expectedPayload.Key);
                        if (thumbnailKeys.All(k => k != thumbnailKey))
                        {
                            throw new OdinClientException("Not all payloads received");
                        }
                    }
                }
            }

            var responseCode = await FinalizeTransferInternal(fileMetadata, odinContext);

            if (responseCode == PeerResponseCode.AcceptedDirectWrite || responseCode == PeerResponseCode.AcceptedIntoInbox)
            {
                var notificationOptions = _updateInstructionSet.Request.AppNotificationOptions;
                if (null != notificationOptions)
                {
                    var senderId = odinContext.GetCallerOdinIdOrFail();
                    var newContext = OdinContextUpgrades.UpgradeToPeerTransferContext(odinContext);
                    await pushNotificationService.EnqueueNotification(senderId, notificationOptions, newContext);
                }

                return new PeerTransferResponse() { Code = responseCode };
            }

            throw new OdinSystemException("Unhandled Routing");
        }

        public async Task CleanupStagingFiles(List<PayloadDescriptor> descriptors, IOdinContext odinContext)
        {
            if (_isDirectWrite)
                await fileSystem.Storage.CleanupUploadTemporaryFiles(_file, descriptors, odinContext);
        }

        //

        private async Task<PeerResponseCode> FinalizeTransferInternal(FileMetadata fileMetadata, IOdinContext odinContext)
        {
            //S0001, S1000, S2000 - can the sender write the content to the target drive?
            await fileSystem.Storage.AssertCanWriteToDrive(_file.DriveId, odinContext);

            var directWriteSuccess = await TryDirectWriteFileAsync(fileMetadata, odinContext);

            if (directWriteSuccess)
            {
                return PeerResponseCode.AcceptedDirectWrite;
            }

            logger.LogDebug("TryDirectWrite failed for file ({file}) - routing to inbox.", _file);

            //S1220 - the instruction set and metadata travel on the inbox row (no inbox folder); payloads are
            // already in long-term storage under the incoming fileId.
            return await RouteToInboxAsync(fileMetadata, odinContext);
        }

        private async Task WriteInstructionsAndMetadataToDisk(FileMetadata fileMetadata, IOdinContext odinContext)
        {
            logger.LogDebug("Writing metadata for file {file} (isDirectWrite: {isDirectWrite})", _file, _isDirectWrite);

            // Inbox-routed transfers stage nothing on disk: the instruction set and metadata travel on the inbox
            // row (TransferInboxItem.Data / .FileMetadata) and payloads stream straight to long-term. Only the
            // direct-write path still uses the upload staging folder.
            if (!_isDirectWrite)
            {
                return;
            }

            // Write the instruction set to disk
            await using var stream = new MemoryStream(OdinSystemSerializer.Serialize(_updateInstructionSet).ToUtf8ByteArray());
            await fileSystem.Storage.WriteUploadStream(_file, TenantPathManager.TransferInstructionSetExtension, stream, odinContext);

            var metadataStream = new MemoryStream(Encoding.UTF8.GetBytes(OdinSystemSerializer.Serialize(fileMetadata)));
            await fileSystem.Storage.WriteUploadStream(_file, TenantPathManager.MetadataExtension, metadataStream, odinContext);
        }

        private async Task<bool> TryDirectWriteFileAsync(FileMetadata metadata, IOdinContext odinContext)
        {
            if (!await CanDirectWriteFile(_file.DriveId, metadata, _updateInstructionSet.FileSystemType, odinContext))
            {
                return false;
            }

            PeerFileUpdateWriter updateWriter = new PeerFileUpdateWriter(logger, fileSystemResolver, driveManager);
            var sender = odinContext.GetCallerOdinIdOrFail();
            var decryptedKeyHeader = DecryptKeyHeaderWithSharedSecret(_updateInstructionSet.EncryptedKeyHeader, odinContext);
            var sourceArea = _isDirectWrite ? StagingArea.Upload : StagingArea.Inbox;

            if (metadata.IsEncrypted == false)
            {
                //S1110 - Write to disk and send notifications
                await updateWriter.UpsertFileAsync(_file, decryptedKeyHeader, sender, _updateInstructionSet, odinContext, null,
                    sourceArea: sourceArea);
                return true;
            }

            //S1100
            if (metadata.IsEncrypted)
            {
                // Next determine if we can direct write the file
                var hasStorageKey = odinContext.PermissionsContext.TryGetDriveStorageKey(_file.DriveId, out _);

                //S1200
                if (hasStorageKey)
                {
                    //S1205
                    await updateWriter.UpsertFileAsync(_file, decryptedKeyHeader, sender, _updateInstructionSet, odinContext, null,
                        sourceArea: sourceArea);
                    return true;
                }

                //S2210 - comments cannot fall back to inbox
                if (_updateInstructionSet.FileSystemType == FileSystemType.Comment)
                {
                    throw new OdinSecurityException("Sender cannot write the comment");
                }
            }

            return false;
        }

        private KeyHeader DecryptKeyHeaderWithSharedSecret(EncryptedKeyHeader sharedSecretEncryptedKeyHeader, IOdinContext odinContext)
        {
            var sharedSecret = odinContext.PermissionsContext.SharedSecretKey;
            var decryptedKeyHeader = sharedSecretEncryptedKeyHeader.DecryptAesToKeyHeader(ref sharedSecret);
            return decryptedKeyHeader;
        }

        /// <summary>
        /// Stores the file in the inbox so it can be processed by the owner in a separate process
        /// </summary>
        private async Task<PeerResponseCode> RouteToInboxAsync(FileMetadata fileMetadata, IOdinContext odinContext)
        {
            var item = new TransferInboxItem
            {
                Id = Guid.NewGuid(),
                AddedTimestamp = UnixTimeUtc.Now(),
                Sender = odinContext.GetCallerOdinIdOrFail(),
                Priority = 500,

                InstructionType = TransferInstructionType.UpdateFile,
                DriveId = _file.DriveId,
                FileId = _file.FileId,
                FileSystemType = _updateInstructionSet.FileSystemType,
                Data = OdinSystemSerializer.Serialize(_updateInstructionSet).ToUtf8ByteArray(),

                // The incoming metadata rides on the inbox row instead of an inbox-folder .metadata file.
                FileMetadata = fileMetadata,

                Marker = default,
                GlobalTransitId = default,
                TransferInstructionSet = null,
                EncryptedFeedPayload = null,
                TransferFileType = TransferFileType.Normal,
                SharedSecretEncryptedKeyHeader = null
            };

            // The payloads were already streamed straight to long-term under the incoming fileId during receive.
            // If enqueueing the inbox item fails, nothing will ever process or clean them (the orphan scanner only
            // sweeps the inbox folder, not long-term), so reclaim them here before failing the update.
            try
            {
                await transitInboxBoxStorage.AddAsync(item);
            }
            catch (Exception e)
            {
                // Whatever the cause (cancellation included), the item never enqueued, so the payloads streamed to
                // long-term under the incoming fileId are now orphans that nothing reclaims (the orphan scanner only
                // sweeps the inbox folder, not long-term). Clean them up best-effort before propagating. Cancellation
                // is not a failure, so it is not logged as an error, but its orphans still have to be reclaimed.
                if (e is not OperationCanceledException)
                {
                    logger.LogError(e, "Failed to enqueue inbox item for file {file}; cleaning up directly-written " +
                                       "long-term payloads to avoid orphans", _file);
                }

                try
                {
                    await fileSystem.Storage.CleanupAbandonedLongTermPayloads(_file, fileMetadata.Payloads, odinContext);
                }
                catch (Exception cleanupEx)
                {
                    logger.LogError(cleanupEx, "Cleanup of directly-written long-term payloads failed for file {file}", _file);
                }

                throw;
            }

            await mediator.Publish(new InboxItemReceivedNotification()
            {
                TargetDrive = (await driveManager.GetDriveAsync(item.DriveId)).TargetDriveInfo,
                TransferFileType = TransferFileType.Normal,
                FileSystemType = item.FileSystemType,
            });

            return PeerResponseCode.AcceptedIntoInbox;
        }

        private async Task<bool> CanDirectWriteFile(Guid driveId,
            FileMetadata metadata,
            FileSystemType fileSystemType,
            IOdinContext odinContext)
        {
            //HACK: if it's not a connected token
            if (odinContext.AuthContext.ToLower() != "TransitCapiAuthScheme".ToLower())
            {
                return false;
            }

            if (metadata.IsEncrypted == false)
            {
                return true;
            }

            //S1100
            if (metadata.IsEncrypted && odinContext.PermissionsContext.TryGetDriveStorageKey(driveId, out _))
            {
                return true;
            }

            //S2210 - comments cannot fall back to inbox
            if (fileSystemType == FileSystemType.Comment)
            {
                throw new OdinSecurityException($"Sender cannot direct-write the comment to drive {driveId}");
            }

            await Task.CompletedTask;
            return false;
        }
    }
}