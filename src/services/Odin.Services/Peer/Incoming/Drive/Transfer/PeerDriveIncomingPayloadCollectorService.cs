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
using Odin.Core.Storage.SQLite;
using Odin.Core.Time;
using Odin.Services.AppNotifications.Push;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.Drives.FileSystem;
using Odin.Services.Drives.FileSystem.Base.Upload;
using Odin.Services.Drives.FileSystem.Base.Upload.Attachments;
using Odin.Services.Mediator;
using Odin.Services.Peer.Incoming.Drive.Transfer.InboxStorage;
using Odin.Services.Peer.Outgoing.Drive;
using Odin.Services.Peer.Outgoing.Drive.Transfer;

namespace Odin.Services.Peer.Incoming.Drive.Transfer
{
    public class PeerDriveIncomingPayloadCollectorService(
        ILogger<PeerDriveIncomingPayloadCollectorService> logger,
        IDriveFileSystem fileSystem,
        TenantSystemStorage tenantSystemStorage,
        IMediator mediator,
        PushNotificationService pushNotificationService)
    {
        private PayloadTransferInstructionSet _transferInstructionSet;
        private InternalDriveFileId _sourceTempFile;
        private InternalDriveFileId _targetFile;

        private PayloadOnlyPackage _package;

        private readonly TransitInboxBoxStorage _transitInboxBoxStorage = new(tenantSystemStorage);
        private readonly Dictionary<string, List<string>> _uploadedKeys = new(StringComparer.InvariantCultureIgnoreCase);

        public async Task InitializeIncomingTransfer(PayloadTransferInstructionSet instructionSet, IOdinContext odinContext,
            DatabaseConnection cn)
        {
            var driveId = odinContext.PermissionsContext.GetDriveId(instructionSet.TargetFile.Drive);

            //validate the target file exists by gtid
            var existingFile = await fileSystem.Query.GetFileByGlobalTransitId(driveId, instructionSet.TargetFile.FileId, odinContext, cn);
            if (null == existingFile)
            {
                throw new OdinClientException("No file found by GlobalTransitId");
            }

            _targetFile = new InternalDriveFileId()
            {
                FileId = existingFile.FileId,
                DriveId = driveId
            };

            _sourceTempFile = new InternalDriveFileId()
            {
                FileId = Guid.NewGuid(),
                DriveId = driveId
            };

            _transferInstructionSet = instructionSet;
            
            this._package = new PayloadOnlyPackage(file, instructionSet!);

            // Write the instruction set to disk
            await using var stream = new MemoryStream(OdinSystemSerializer.Serialize(instructionSet).ToUtf8ByteArray());
            await fileSystem.Storage.WriteTempStream(_sourceTempFile, MultipartHostTransferParts.PayloadTransferInstructionSet.ToString().ToLower(), stream,
                odinContext, cn);
        }

        public async Task AcceptPayload(string key, string fileExtension, Stream data, IOdinContext odinContext,
            DatabaseConnection cn)
        {
            _uploadedKeys.TryAdd(key, new List<string>());
            var bytesWritten = await fileSystem.Storage.WriteTempStream(_sourceTempFile, fileExtension, data, odinContext, cn);
            if (bytesWritten > 0)
            {
                _package.Payloads.Add(new PackagePayloadDescriptor()
                {
                    Iv = descriptor.Iv,
                    PayloadKey = key,
                    Uid = descriptor.PayloadUid,
                    ContentType = descriptor.ContentType,
                    LastModified = UnixTimeUtc.Now(),
                    BytesWritten = bytesWritten,
                    DescriptorContent = descriptor.DescriptorContent,
                    PreviewThumbnail = descriptor.PreviewThumbnail
                });
            }
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

            await fileSystem.Storage.WriteTempStream(_sourceTempFile, fileExtension, data, odinContext, cn);
        }

        public async Task<PeerTransferResponse> FinalizeTransfer(IOdinContext odinContext, DatabaseConnection cn)
        {
            // Validate we received all expected payloads
            foreach (var expectedPayload in _transferInstructionSet.Manifest.PayloadDescriptors)
            {
                var hasPayload = _uploadedKeys.TryGetValue(expectedPayload.PayloadKey, out var thumbnailKeys);
                if (!hasPayload)
                {
                    throw new OdinClientException("Not all payloads received");
                }

                foreach (var expectedThumbnail in expectedPayload.Thumbnails)
                {
                    var thumbnailKey = expectedThumbnail.CreateTransitKey(expectedPayload.PayloadKey);
                    if (thumbnailKeys.All(k => k != thumbnailKey))
                    {
                        throw new OdinClientException("Not all payloads received");
                    }
                }
            }

            await RouteToInbox(odinContext, cn);

            if (null != _transferInstructionSet.AppNotificationOptions)
            {
                var senderId = odinContext.GetCallerOdinIdOrFail();
                var newContext = OdinContextUpgrades.UpgradeToPeerTransferContext(odinContext);
                await pushNotificationService.EnqueueNotification(senderId, _transferInstructionSet.AppNotificationOptions, newContext, cn);
            }

            return new PeerTransferResponse() { Code = PeerResponseCode.AcceptedIntoInbox };
        }

        //

        /// <summary>
        /// Stores the file in the inbox, so it can be processed by the owner in a separate process
        /// </summary>
        private async Task RouteToInbox(IOdinContext odinContext, DatabaseConnection cn)
        {
            var item = new TransferInboxItem()
            {
                Id = Guid.NewGuid(),
                AddedTimestamp = UnixTimeUtc.Now(),
                Sender = odinContext.GetCallerOdinIdOrFail(),

                InstructionType = TransferInstructionType.SavePayloads,
                DriveId = _targetFile.DriveId,
                FileId = _targetFile.FileId,

                PayloadSourceFile = _sourceTempFile,
                PayloadInstructionSet = _transferInstructionSet,

                FileSystemType = _transferInstructionSet.FileSystemType,

                TransferInstructionSet = default,
                TransferFileType = default,
                SharedSecretEncryptedKeyHeader = default,
                EncryptedFeedPayload = default
            };

            await _transitInboxBoxStorage.Add(item, cn);
            await mediator.Publish(new InboxItemReceivedNotification()
            {
                TargetDrive = _transferInstructionSet.TargetFile.Drive,
                TransferFileType = TransferFileType.Normal,
                FileSystemType = item.FileSystemType,
                OdinContext = odinContext,
                DatabaseConnection = cn
            });
        }
    }
}