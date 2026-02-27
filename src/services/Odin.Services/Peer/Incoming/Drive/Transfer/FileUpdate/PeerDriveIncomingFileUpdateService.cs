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
        private UploadFile _uploadFile;
        private InboxFile _inboxFile;

        private InternalDriveFileId? CurrentFileId => _uploadFile?.FileId ?? _inboxFile?.FileId;
        private Guid CurrentDriveId => CurrentFileId?.DriveId ?? Guid.Empty;
        private bool IsUpload => _uploadFile != null;

        private readonly Dictionary<string, List<string>> _uploadedKeys = new(StringComparer.InvariantCultureIgnoreCase);

        public async Task InitializeIncomingTransfer(EncryptedRecipientFileUpdateInstructionSet transferInstructionSet,
            FileMetadata fileMetadata,
            IOdinContext odinContext)
        {
            var driveId = transferInstructionSet.Request.File.TargetDrive.Alias;
            var canDirectWrite = await CanDirectWriteFile(driveId, fileMetadata, transferInstructionSet.FileSystemType, odinContext);

            var internalFileId = await fileSystem.Storage.CreateInternalFileId(driveId, odinContext);
            if (canDirectWrite)
            {
                _uploadFile = new UploadFile(internalFileId);
            }
            else
            {
                _inboxFile = new InboxFile(internalFileId);
            }

            _updateInstructionSet = transferInstructionSet;
            await WriteInstructionsAndMetadataToDisk(fileMetadata, odinContext);
        }

        public async Task AcceptPayload(string key, string fileExtension, Stream data, IOdinContext odinContext)
        {
            _uploadedKeys.TryAdd(key, new List<string>());
            await fileSystem.Storage.WriteUploadTempStream(_uploadFile, fileExtension, data, odinContext);
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

            await fileSystem.Storage.WriteUploadTempStream(_uploadFile, fileExtension, data, odinContext);
        }

        public async Task<PeerTransferResponse> FinalizeTransfer(FileMetadata fileMetadata, IOdinContext odinContext)
        {
            // Validate that all expected payloads and their thumbnails have been received during the update transfer
            if (fileMetadata.Payloads?.Any() ?? false)
            {
                foreach (var expectedPayload in fileMetadata.Payloads)
                {
                    // Check if the payload key was uploaded
                    var hasPayload = _uploadedKeys.TryGetValue(expectedPayload.Key, out var thumbnailKeys);
                    if (!hasPayload)
                    {
                        throw new OdinClientException("Not all payloads received");
                    }

                    // For each expected thumbnail, verify it was uploaded under this payload
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

        public async Task CleanupTempFiles(List<PayloadDescriptor> descriptors, IOdinContext odinContext)
        {
            await fileSystem.Storage.CleanupUploadTemporaryFiles(this._uploadFile, descriptors, odinContext);
        }

        //

        private async Task<PeerResponseCode> FinalizeTransferInternal(FileMetadata fileMetadata, IOdinContext odinContext)
        {
            // Assert that the sender has write permissions to the target drive
            await fileSystem.Storage.AssertCanWriteToDrive(_uploadFile.FileId.DriveId, odinContext);

            // Attempt to write the file directly to the drive
            var directWriteSuccess = await TryDirectWriteFileAsync(fileMetadata, odinContext);

            if (directWriteSuccess)
            {
                return PeerResponseCode.AcceptedDirectWrite;
            }

            // If direct write failed, fall back to inbox: create inbox file and write metadata/instructions
            logger.LogDebug("TryDirectWrite failed for file ({file}) - falling back to inbox. Writing metadata to inbox",
                _uploadFile);

            try
            {
                _inboxFile = new InboxFile(_uploadFile.FileId);
                await WriteInstructionsAndMetadataToDisk(fileMetadata, odinContext);
            }
            catch (Exception e)
            {
                logger.LogError(e, "After TryDirectWriteFailed, we also failed to ensure " +
                                    "metadata and instructions are available to the inbox.  file: {tempFile}", _uploadFile.FileId);
            }

            // Route the file to the inbox for later processing
            return await RouteToInboxAsync(odinContext);
        }

        private async Task WriteInstructionsAndMetadataToDisk(FileMetadata fileMetadata, IOdinContext odinContext)
        {
            logger.LogDebug("Writing metadata as {tempFile}", _uploadFile);

            // Write the instruction set to disk
            await using var stream = new MemoryStream(OdinSystemSerializer.Serialize(_updateInstructionSet).ToUtf8ByteArray());
            await fileSystem.Storage.WriteUploadTempStream(_uploadFile, TenantPathManager.TransferInstructionSetExtension, stream,
                odinContext);

            var metadataStream = new MemoryStream(Encoding.UTF8.GetBytes(OdinSystemSerializer.Serialize(fileMetadata)));
            await fileSystem.Storage.WriteUploadTempStream(_uploadFile, TenantPathManager.MetadataExtension, metadataStream, odinContext);
        }

        private async Task<bool> TryDirectWriteFileAsync(FileMetadata metadata, IOdinContext odinContext)
        {
            if (!await CanDirectWriteFile(_uploadFile.FileId.DriveId, metadata, _updateInstructionSet.FileSystemType, odinContext))
            {
                return false;
            }

            PeerFileUpdateWriter updateWriter = new PeerFileUpdateWriter(logger, fileSystemResolver, driveManager);
            var sender = odinContext.GetCallerOdinIdOrFail();
            var decryptedKeyHeader = DecryptKeyHeaderWithSharedSecret(_updateInstructionSet.EncryptedKeyHeader, odinContext);

            if (metadata.IsEncrypted == false)
            {
                //S1110 - Write to disk and send notifications
                await updateWriter.UpsertFileAsync(_uploadFile, decryptedKeyHeader, sender, _updateInstructionSet, odinContext, null);
                return true;
            }

            //S1100
            if (metadata.IsEncrypted)
            {
                // Next determine if we can direct write the file
                var hasStorageKey = odinContext.PermissionsContext.TryGetDriveStorageKey(_uploadFile.FileId.DriveId, out _);

                //S1200
                if (hasStorageKey)
                {
                    //S1205
                    await updateWriter.UpsertFileAsync(_uploadFile, decryptedKeyHeader, sender, _updateInstructionSet, odinContext, null);
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
        private async Task<PeerResponseCode> RouteToInboxAsync(IOdinContext odinContext)
        {
            var item = new TransferInboxItem
            {
                Id = Guid.NewGuid(),
                AddedTimestamp = UnixTimeUtc.Now(),
                Sender = odinContext.GetCallerOdinIdOrFail(),
                Priority = 500,

                InstructionType = TransferInstructionType.UpdateFile,
                DriveId = _uploadFile.FileId.DriveId,
                FileId = _uploadFile.FileId.FileId,
                FileSystemType = _updateInstructionSet.FileSystemType,
                Data = OdinSystemSerializer.Serialize(_updateInstructionSet).ToUtf8ByteArray(),

                Marker = default,
                GlobalTransitId = default,
                TransferInstructionSet = null,
                EncryptedFeedPayload = null,
                TransferFileType = TransferFileType.Normal,
                SharedSecretEncryptedKeyHeader = null
            };

            await transitInboxBoxStorage.AddAsync(item);
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
            if (odinContext.AuthContext.ToLower() != "TransitCertificate".ToLower())
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