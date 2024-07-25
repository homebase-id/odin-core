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
        private PeerPayloadPackage _package;
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

            var file = new InternalDriveFileId()
            {
                FileId = existingFile.FileId,
                DriveId = driveId
            };

            _package = new PeerPayloadPackage(file, instructionSet);

            // Write the instruction set to disk
            await using var stream = new MemoryStream(OdinSystemSerializer.Serialize(instructionSet).ToUtf8ByteArray());
            await fileSystem.Storage.WriteTempStream(_package.TempFile, MultipartHostTransferParts.PayloadTransferInstructionSet.ToString().ToLower(), stream,
                odinContext, cn);
        }

        public async Task AcceptPayload(string key, string fileExtension, Stream data, IOdinContext odinContext,
            DatabaseConnection cn)
        {
            var descriptor = _package.InstructionSet.Manifest?.PayloadDescriptors.SingleOrDefault(pd => pd.PayloadKey == key);

            if (null == descriptor)
            {
                throw new OdinClientException($"Cannot find descriptor for payload key {key}", OdinClientErrorCode.InvalidUpload);
            }

            if (_package.Payloads.Any(p => string.Equals(key, p.PayloadKey, StringComparison.InvariantCultureIgnoreCase)))
            {
                throw new OdinClientException("Duplicate payload keys", OdinClientErrorCode.InvalidUpload);
            }

            _uploadedKeys.TryAdd(key, new List<string>());
            var bytesWritten = await fileSystem.Storage.WriteTempStream(_package.TempFile, fileExtension, data, odinContext, cn);
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
            // if you're adding a thumbnail, there must be a manifest
            var descriptors = _package.InstructionSet.Manifest?.PayloadDescriptors;
            if (null == descriptors)
            {
                throw new OdinClientException("An upload manifest with payload descriptors is required when you're adding thumbnails");
            }

            var result = descriptors.Select(pd =>
            {
                return new
                {
                    pd.PayloadKey,
                    pd.PayloadUid,
                    ThumbnailDescriptor = pd.Thumbnails?.SingleOrDefault(th => th.ThumbnailKey == thumbnailKey)
                };
            }).SingleOrDefault(p => p.ThumbnailDescriptor != null);

            if (null == result)
            {
                throw new OdinClientException(
                    $"Error while adding thumbnail; the upload manifest does not " +
                    $"have a thumbnail descriptor matching key {thumbnailKey}",
                    OdinClientErrorCode.InvalidUpload);
            }

            if (!_uploadedKeys.TryGetValue(payloadKey, out var thumbnailKeys))
            {
                thumbnailKeys = new List<string>();
                _uploadedKeys.Add(payloadKey, thumbnailKeys);
            }

            thumbnailKeys.Add(thumbnailKey);
            _uploadedKeys[payloadKey] = thumbnailKeys;

            var bytesWritten = await fileSystem.Storage.WriteTempStream(_package.TempFile, fileExtension, data, odinContext, cn);

            if (bytesWritten > 0)
            {
                _package.Thumbnails.Add(new PackageThumbnailDescriptor()
                {
                    PixelHeight = result.ThumbnailDescriptor.PixelHeight,
                    PixelWidth = result.ThumbnailDescriptor.PixelWidth,
                    ContentType = result.ThumbnailDescriptor.ContentType,
                    PayloadKey = result.PayloadKey,
                    BytesWritten = bytesWritten
                });
            }
        }

        public async Task<PeerTransferResponse> FinalizeTransfer(IOdinContext odinContext, DatabaseConnection cn)
        {
            logger.LogDebug("Finalizing Transfer");

            // Validate we received all expected payloads
            foreach (var expectedPayload in _package.InstructionSet.Manifest.PayloadDescriptors)
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

            if (null != _package.InstructionSet.AppNotificationOptions)
            {
                var senderId = odinContext.GetCallerOdinIdOrFail();
                var newContext = OdinContextUpgrades.UpgradeToPeerTransferContext(odinContext);
                await pushNotificationService.EnqueueNotification(senderId, _package.InstructionSet.AppNotificationOptions, newContext, cn);
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
                DriveId = _package.InternalFile.DriveId,
                FileId = _package.InternalFile.FileId,

                PeerPayloadPackage = _package,
                FileSystemType = _package.InstructionSet.FileSystemType,
            };

            await _transitInboxBoxStorage.Add(item, cn);
            await mediator.Publish(new InboxItemReceivedNotification()
            {
                TargetDrive = _package.InstructionSet.TargetFile.Drive,
                TransferFileType = TransferFileType.Normal,
                FileSystemType = item.FileSystemType,
                OdinContext = odinContext,
                DatabaseConnection = cn
            });
        }
    }
}