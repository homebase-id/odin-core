using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using MediatR;
using Microsoft.Extensions.Logging;
using Odin.Core;
using Odin.Core.Exceptions;
using Odin.Core.Serialization;
using Odin.Core.Storage;
using Odin.Core.Storage.SQLite;
using Odin.Core.Time;
using Odin.Services.AppNotifications.Push;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.FileSystem;
using Odin.Services.Drives.Management;
using Odin.Services.Mediator;
using Odin.Services.Peer.Encryption;
using Odin.Services.Peer.Incoming.Drive.Transfer.InboxStorage;
using Odin.Services.Peer.Outgoing.Drive;

namespace Odin.Services.Peer.Incoming.Drive.Transfer.FileUpdate
{
    public class PeerDriveIncomingFileUpdateService(
        ILogger<PeerDriveIncomingFileUpdateService> logger,
        DriveManager driveManager,
        IDriveFileSystem fileSystem,
        TenantSystemStorage tenantSystemStorage,
        IMediator mediator,
        FileSystemResolver fileSystemResolver,
        PushNotificationService pushNotificationService)
    {
        private EncryptedRecipientFileUpdateInstructionSet _updateInstructionSet;
        private InternalDriveFileId _tempFile;

        private readonly TransitInboxBoxStorage _transitInboxBoxStorage = new(tenantSystemStorage);
        private readonly Dictionary<string, List<string>> _uploadedKeys = new(StringComparer.InvariantCultureIgnoreCase);

        public async Task InitializeIncomingTransfer(EncryptedRecipientFileUpdateInstructionSet transferInstructionSet, IOdinContext odinContext,
            DatabaseConnection cn)
        {
            var driveId = odinContext.PermissionsContext.GetDriveId(transferInstructionSet.Request.File.TargetDrive);

            // Notice here: we always create a new fileId when receiving a new file.
            _tempFile = await fileSystem.Storage.CreateInternalFileId(driveId, cn);
            _updateInstructionSet = transferInstructionSet;

            // Write the instruction set to disk
            await using var stream = new MemoryStream(OdinSystemSerializer.Serialize(transferInstructionSet).ToUtf8ByteArray());
            await fileSystem.Storage.WriteTempStream(_tempFile, MultipartHostTransferParts.TransferKeyHeader.ToString().ToLower(), stream, odinContext, cn);
        }

        public async Task AcceptMetadata(string fileExtension, Stream data, IOdinContext odinContext,
            DatabaseConnection cn)
        {
            await fileSystem.Storage.WriteTempStream(_tempFile, fileExtension, data, odinContext, cn);
        }

        public async Task AcceptPayload(string key, string fileExtension, Stream data, IOdinContext odinContext,
            DatabaseConnection cn)
        {
            _uploadedKeys.TryAdd(key, new List<string>());
            await fileSystem.Storage.WriteTempStream(_tempFile, fileExtension, data, odinContext, cn);
        }

        public async Task AcceptThumbnail(string payloadKey, string thumbnailKey, string fileExtension, Stream data, IOdinContext odinContext,
            DatabaseConnection cn)
        {
            if (!_uploadedKeys.TryGetValue(payloadKey, out var thumbnailKeys))
            {
                thumbnailKeys = new List<string>();
                _uploadedKeys.Add(payloadKey, thumbnailKeys);
            }

            thumbnailKeys.Add(thumbnailKey);
            _uploadedKeys[payloadKey] = thumbnailKeys;

            await fileSystem.Storage.WriteTempStream(_tempFile, fileExtension, data, odinContext, cn);
        }

        public async Task<PeerTransferResponse> FinalizeTransfer(FileMetadata fileMetadata, IOdinContext odinContext,
            DatabaseConnection cn)
        {
            // if there are payloads in the descriptor, be sure we got it all
            if (fileMetadata.Payloads.Any())
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

            var responseCode = await FinalizeTransferInternal(fileMetadata, odinContext, cn);

            if (responseCode == PeerResponseCode.AcceptedDirectWrite || responseCode == PeerResponseCode.AcceptedIntoInbox)
            {
                var notificationOptions = _updateInstructionSet.Request.AppNotificationOptions;
                if (null != notificationOptions)
                {
                    var senderId = odinContext.GetCallerOdinIdOrFail();
                    var newContext = OdinContextUpgrades.UpgradeToPeerTransferContext(odinContext);
                    await pushNotificationService.EnqueueNotification(senderId, notificationOptions, newContext, cn);
                }

                return new PeerTransferResponse() { Code = responseCode };
            }

            throw new OdinSystemException("Unhandled Routing");
        }

        //

        private async Task<PeerResponseCode> FinalizeTransferInternal(FileMetadata fileMetadata, IOdinContext odinContext, DatabaseConnection cn)
        {
            //S0001, S1000, S2000 - can the sender write the content to the target drive?
            await fileSystem.Storage.AssertCanWriteToDrive(_tempFile.DriveId, odinContext, cn);

            var directWriteSuccess = await TryDirectWriteFile(fileMetadata, odinContext, cn);

            if (directWriteSuccess)
            {
                return PeerResponseCode.AcceptedDirectWrite;
            }

            //S1220
            return await RouteToInbox(odinContext, cn);
        }

        private async Task<bool> TryDirectWriteFile(FileMetadata metadata, IOdinContext odinContext, DatabaseConnection cn)
        {
            await fileSystem.Storage.AssertCanWriteToDrive(_tempFile.DriveId, odinContext, cn);

            //HACK: if it's not a connected token
            if (odinContext.AuthContext.ToLower() != "TransitCertificate".ToLower())
            {
                return false;
            }

            //TODO: check if any apps are online and we can snag the storage key

            PeerFileUpdateWriter updateWriter = new PeerFileUpdateWriter(logger, fileSystemResolver, driveManager);
            var sender = odinContext.GetCallerOdinIdOrFail();
            var decryptedKeyHeader = DecryptKeyHeaderWithSharedSecret(_updateInstructionSet.EncryptedKeyHeaderIvOnly, odinContext);

            if (metadata.IsEncrypted == false)
            {
                //S1110 - Write to disk and send notifications
                await updateWriter.UpdateFile(_tempFile, decryptedKeyHeader, sender, _updateInstructionSet, odinContext, cn);
                return true;
            }

            //S1100
            if (metadata.IsEncrypted)
            {
                // Next determine if we can direct write the file
                var hasStorageKey = odinContext.PermissionsContext.TryGetDriveStorageKey(_tempFile.DriveId, out var _);

                //S1200
                if (hasStorageKey)
                {
                    //S1205
                    await updateWriter.UpdateFile(_tempFile, decryptedKeyHeader, sender, _updateInstructionSet, odinContext, cn);
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
        private async Task<PeerResponseCode> RouteToInbox(IOdinContext odinContext, DatabaseConnection cn)
        {
            var item = new TransferInboxItem
            {
                Id = Guid.NewGuid(),
                AddedTimestamp = UnixTimeUtc.Now(),
                Sender = odinContext.GetCallerOdinIdOrFail(),
                Priority = 0,

                InstructionType = TransferInstructionType.UpdateFile,
                DriveId = _tempFile.DriveId,
                FileId = _tempFile.FileId,
                FileSystemType = _updateInstructionSet.FileSystemType,
                Data = OdinSystemSerializer.Serialize(_updateInstructionSet).ToUtf8ByteArray(),
                
                Marker = default,
                GlobalTransitId = default,
                TransferInstructionSet = null,
                EncryptedFeedPayload = null,
                TransferFileType = TransferFileType.Normal,
                SharedSecretEncryptedKeyHeader = null
            };

            await _transitInboxBoxStorage.Add(item, cn);
            await mediator.Publish(new InboxItemReceivedNotification()
            {
                TargetDrive = driveManager.GetDrive(item.DriveId, cn).Result.TargetDriveInfo,
                TransferFileType = TransferFileType.Normal,
                FileSystemType = item.FileSystemType,
                OdinContext = odinContext,
                DatabaseConnection = cn
            });

            return PeerResponseCode.AcceptedIntoInbox;
        }
    }
}