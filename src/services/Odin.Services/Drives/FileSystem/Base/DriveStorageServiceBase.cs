using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.Extensions.Logging;
using Odin.Core;
using Odin.Core.Exceptions;
using Odin.Core.Identity;
using Odin.Core.Serialization;
using Odin.Core.Storage;
using Odin.Core.Time;
using Odin.Core.Util;
using Odin.Services.Apps;
using Odin.Services.Authorization.Acl;
using Odin.Services.Base;
using Odin.Services.Configuration;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.Management;
using Odin.Services.Mediator;
using Odin.Services.Peer.Encryption;

namespace Odin.Services.Drives.FileSystem.Base
{
    public abstract class DriveStorageServiceBase(
        IOdinContextAccessor contextAccessor,
        ILoggerFactory loggerFactory,
        IMediator mediator,
        IDriveAclAuthorizationService driveAclAuthorizationService,
        DriveManager driveManager,
        OdinConfiguration odinConfiguration,
        DriveFileReaderWriter driveFileReaderWriter,
        ConcurrentFileManager concurrentFileManager)
        : RequirePermissionsBase
    {
        private readonly ILogger<DriveStorageServiceBase> _logger = loggerFactory.CreateLogger<DriveStorageServiceBase>();

        protected override DriveManager DriveManager { get; } = driveManager;
        protected override IOdinContextAccessor ContextAccessor { get; } = contextAccessor;

        /// <summary>
        /// Gets the <see cref="FileSystemType"/> the inheriting class manages
        /// </summary>
        public abstract FileSystemType GetFileSystemType();

        public async Task<SharedSecretEncryptedFileHeader> GetSharedSecretEncryptedHeader(InternalDriveFileId file)
        {
            var serverFileHeader = await this.GetServerFileHeader(file);
            if (serverFileHeader == null)
            {
                return null;
            }

            var result = DriveFileUtility.ConvertToSharedSecretEncryptedClientFileHeader(serverFileHeader, ContextAccessor.GetCurrent());
            return result;
        }

        /// <summary>
        /// Gets an EncryptedKeyHeader for a given payload using the payload's IV
        /// </summary>
        public async Task<(ServerFileHeader header, PayloadDescriptor payloadDescriptor, EncryptedKeyHeader encryptedKeyHeader, bool fileExists)>
            GetPayloadSharedSecretEncryptedKeyHeader(InternalDriveFileId file, string payloadKey)
        {
            var serverFileHeader = await this.GetServerFileHeader(file);
            if (serverFileHeader == null)
            {
                return (null, null, null, false);
            }

            var payloadDescriptor = serverFileHeader.FileMetadata.GetPayloadDescriptor(payloadKey);
            if (null == payloadDescriptor)
            {
                return (null, null, null, false);
            }

            var payloadEncryptedKeyHeader = DriveFileUtility.GetPayloadEncryptedKeyHeader(
                serverFileHeader,
                payloadDescriptor,
                ContextAccessor);

            return (serverFileHeader, payloadDescriptor, payloadEncryptedKeyHeader, true);
        }

        public async Task<InternalDriveFileId> CreateInternalFileId(Guid driveId)
        {
            var lts = await GetLongTermStorageManager(driveId);
            var df = new InternalDriveFileId()
            {
                FileId = lts.CreateFileId(),
                DriveId = driveId,
            };

            return df;
        }

        public async Task UpdateActiveFileHeader(InternalDriveFileId targetFile, ServerFileHeader header, bool raiseEvent = false)
        {
            await UpdateActiveFileHeaderInternal(targetFile, header, false, raiseEvent);
        }

        /// <summary>
        /// Writes a new file header w/o checking for an existing one
        /// </summary>
        public async Task WriteNewFileHeader(InternalDriveFileId targetFile, ServerFileHeader header, bool raiseEvent = false)
        {
            if (!header.IsValid())
            {
                throw new OdinSystemException("An invalid header was passed to the update header method.  You need more checks in place before getting here");
            }

            await AssertCanWriteToDrive(targetFile.DriveId);

            var metadata = header.FileMetadata;

            //TODO: need to encrypt the metadata parts
            metadata.File = targetFile; //TBH it's strange having this but we need the metadata to have the file and drive embedded
            metadata.Created = header.FileMetadata.Created != 0 ? header.FileMetadata.Created : UnixTimeUtc.Now().milliseconds;
            metadata.FileState = FileState.Active;

            await WriteFileHeaderInternal(header);

            //clean up temp storage
            var tsm = await GetTempStorageManager(targetFile.DriveId);
            await tsm.EnsureDeleted(targetFile.FileId);

            //HACKed in for Feed drive
            if (raiseEvent)
            {
                if (await ShouldRaiseDriveEvent(targetFile))
                {
                    await mediator.Publish(new DriveFileAddedNotification(ContextAccessor.GetCurrent())
                    {
                        File = targetFile,
                        ServerFileHeader = header
                    });
                }
            }
        }


        /// <summary>
        /// Updates the transfer history for this file for the given recipient
        /// </summary>
        public async Task UpdateTransferHistory(InternalDriveFileId file, OdinId recipient, Guid? versionTag, LatestStatus problemStatus)
        {
            await concurrentFileManager.WriteFile(file.ToString(), async _ =>
            {
                var header = await this.GetServerFileHeader(file);

                var history = header.ServerMetadata.TransferHistory ?? new RecipientTransferHistory();
                history.Recipients ??= new Dictionary<string, RecipientTransferHistoryItem>(StringComparer.InvariantCultureIgnoreCase);

                if (!history.Recipients.TryGetValue(recipient, out var recipientItem))
                {
                    recipientItem = new RecipientTransferHistoryItem();
                    history.Recipients.Add(recipient, recipientItem);
                }

                recipientItem.LastUpdated = UnixTimeUtc.Now();
                recipientItem.LatestStatus = problemStatus;
                if (problemStatus == null)
                {
                    recipientItem.LatestSuccessfullyDeliveredVersionTag = versionTag.GetValueOrDefault();
                }

                header.ServerMetadata.TransferHistory = history;
                await this.UpdateActiveFileHeaderInternal(file, header, keepSameVersionTag: true, raiseEvent: true);
            });
        }

        public async Task<uint> WriteTempStream(InternalDriveFileId file, string extension, Stream stream)
        {
            await AssertCanWriteToDrive(file.DriveId);
            var tsm = await GetTempStorageManager(file.DriveId);
            return await tsm.WriteStream(file.FileId, extension, stream);
        }

        /// <summary>
        /// Reads the whole file so be sure this is only used on small'ish files; ones you're ok with loaded fully into server-memory
        /// </summary>
        /// <returns></returns>
        public async Task<byte[]> GetAllFileBytes(InternalDriveFileId file, string extension)
        {
            await this.AssertCanReadDrive(file.DriveId);
            var tsm = await GetTempStorageManager(file.DriveId);
            var bytes = await tsm.GetAllFileBytes(file.FileId, extension);
            return bytes;
        }

        public async Task<byte[]> GetAllFileBytesForWriting(InternalDriveFileId file, string extension)
        {
            await AssertCanWriteToDrive(file.DriveId);
            var tsm = await GetTempStorageManager(file.DriveId);
            return await tsm.GetAllFileBytes(file.FileId, extension);
        }

        public async Task DeleteTempFile(InternalDriveFileId file, string extension)
        {
            await AssertCanWriteToDrive(file.DriveId);
            var tsm = await GetTempStorageManager(file.DriveId);
            await tsm.EnsureDeleted(file.FileId, extension);
        }

        public async Task DeleteTempFiles(InternalDriveFileId file)
        {
            await AssertCanWriteToDrive(file.DriveId);

            var tsm = await GetTempStorageManager(file.DriveId);
            await tsm.EnsureDeleted(file.FileId);
        }


        public async Task<(Stream stream, ThumbnailDescriptor thumbnail)> GetThumbnailPayloadStream(InternalDriveFileId file, int width, int height,
            string payloadKey, UnixTimeUtcUnique payloadUid, bool directMatchOnly = false)
        {
            await AssertCanReadDrive(file.DriveId);

            DriveFileUtility.AssertValidPayloadKey(payloadKey);
            var lts = await GetLongTermStorageManager(file.DriveId);

            //Note: calling to get the file header so we can ensure the caller can read this file
            var header = await this.GetServerFileHeader(file);
            var thumbs = header?.FileMetadata.GetPayloadDescriptor(payloadKey)?.Thumbnails?.ToList();
            if (null == thumbs || !thumbs.Any())
            {
                return (Stream.Null, null);
            }

            var directMatchingThumb = thumbs.SingleOrDefault(t => t.PixelHeight == height && t.PixelWidth == width);
            if (null != directMatchingThumb)
            {
                try
                {
                    var s = await lts.GetThumbnailStream(file.FileId, width, height, payloadKey, payloadUid);
                    return (s, directMatchingThumb);
                }
                catch (OdinFileHeaderHasCorruptPayloadException)
                {
                    var drive = await DriveManager.GetDrive(file.DriveId);
                    if (drive.TargetDriveInfo == SystemDriveConstants.FeedDrive)
                    {
                        return (Stream.Null, directMatchingThumb);
                    }

                    throw;
                }
            }

            if (directMatchOnly)
            {
                return (Stream.Null, null);
            }

            //TODO: add more logic here to compare width and height separately or together
            var nextSizeUp = thumbs.FirstOrDefault(t => t.PixelHeight > height || t.PixelWidth > width);
            if (null == nextSizeUp)
            {
                nextSizeUp = thumbs.LastOrDefault();
                if (null == nextSizeUp)
                {
                    return (Stream.Null, null);
                }
            }

            try
            {
                var stream = await lts.GetThumbnailStream(
                    file.FileId,
                    nextSizeUp.PixelWidth,
                    nextSizeUp.PixelHeight,
                    payloadKey, payloadUid);

                return (stream, nextSizeUp);
            }
            catch (OdinFileHeaderHasCorruptPayloadException)
            {
                var drive = await DriveManager.GetDrive(file.DriveId);
                if (drive.TargetDriveInfo == SystemDriveConstants.FeedDrive)
                {
                    return (Stream.Null, nextSizeUp);
                }

                throw;
            }
        }


        public async Task<Guid> DeletePayload(InternalDriveFileId file, string key, Guid versionTag)
        {
            await AssertCanWriteToDrive(file.DriveId);

            //Note: calling to get the file header so we can ensure the caller can read this file
            var header = await this.GetServerFileHeader(file);
            DriveFileUtility.AssertVersionTagMatch(header.FileMetadata.VersionTag, versionTag);

            var descriptorIndex = header.FileMetadata.Payloads?.FindIndex(p => string.Equals(p.Key, key, StringComparison.InvariantCultureIgnoreCase)) ?? -1;

            if (descriptorIndex == -1)
            {
                return Guid.Empty;
            }

            var lts = await GetLongTermStorageManager(file.DriveId);
            var descriptor = header.FileMetadata.Payloads![descriptorIndex];

            // Delete the thumbnail files for this payload
            foreach (var thumb in descriptor.Thumbnails ?? new List<ThumbnailDescriptor>())
            {
                await lts.DeleteThumbnailFile(file.FileId, key, descriptor.Uid, thumb.PixelWidth, thumb.PixelHeight);
            }

            // Delete the payload file
            await lts.DeletePayloadFile(file.FileId, descriptor);

            header.FileMetadata.Payloads!.RemoveAt(descriptorIndex);
            await UpdateActiveFileHeader(file, header);
            return header.FileMetadata.VersionTag.GetValueOrDefault(); // this works because because pass header all the way
        }

        public async Task<ServerFileHeader> CreateServerFileHeader(InternalDriveFileId file, KeyHeader keyHeader, FileMetadata metadata,
            ServerMetadata serverMetadata)
        {
            return await CreateServerHeaderInternal(file, keyHeader, metadata, serverMetadata);
        }

        private async Task<EncryptedKeyHeader> EncryptKeyHeader(Guid driveId, KeyHeader keyHeader)
        {
            var storageKey = ContextAccessor.GetCurrent().PermissionsContext.GetDriveStorageKey(driveId);

            (await this.DriveManager.GetDrive(driveId)).AssertValidStorageKey(storageKey);

            var encryptedKeyHeader = EncryptedKeyHeader.EncryptKeyHeaderAes(keyHeader, keyHeader.Iv, ref storageKey);
            return encryptedKeyHeader;
        }

        public async Task<bool> CallerHasPermissionToFile(InternalDriveFileId file)
        {
            var lts = await GetLongTermStorageManager(file.DriveId);
            var header = await lts.GetServerFileHeader(file.FileId);

            if (null == header)
            {
                return false;
            }

            return await driveAclAuthorizationService.CallerHasPermission(header.ServerMetadata.AccessControlList);
        }

        public async Task<ServerFileHeader> GetServerFileHeader(InternalDriveFileId file)
        {
            await AssertCanReadDrive(file.DriveId);
            var header = await GetServerFileHeaderInternal(file);

            if (header == null)
            {
                return null;
            }

            AssertValidFileSystemType(header.ServerMetadata);
            return header;
        }

        /// <summary>
        /// Gets the <see cref="FileSystemType"/> of the target file and only enforces the Read
        /// permission; allowing you to determine the file system type when you don't have it.
        /// </summary>
        public async Task<FileSystemType> ResolveFileSystemType(InternalDriveFileId file)
        {
            await AssertCanReadOrWriteToDrive(file.DriveId);

            var header = await GetServerFileHeaderInternal(file);
            return header.ServerMetadata.FileSystemType;
        }

        public async Task<PayloadStream> GetPayloadStream(InternalDriveFileId file, string key, FileChunk chunk)
        {
            await AssertCanReadDrive(file.DriveId);
            DriveFileUtility.AssertValidPayloadKey(key);

            //Note: calling to get the file header will also
            //ensure the caller can touch this file.
            var header = await GetServerFileHeader(file);
            if (header == null)
            {
                return null;
            }

            var descriptor = header.FileMetadata.GetPayloadDescriptor(key);

            if (descriptor == null)
            {
                return null;
            }

            try
            {
                var lts = await GetLongTermStorageManager(file.DriveId);
                var stream = await lts.GetPayloadStream(file.FileId, descriptor, chunk);
                return new PayloadStream(descriptor, stream);
            }
            catch (OdinFileHeaderHasCorruptPayloadException)
            {
                var drive = await DriveManager.GetDrive(file.DriveId);
                if (drive.TargetDriveInfo == SystemDriveConstants.FeedDrive)
                {
                    return null;
                }

                throw;
            }
        }

        public async Task<bool> FileExists(InternalDriveFileId file)
        {
            await AssertCanReadOrWriteToDrive(file.DriveId);
            var lts = await GetLongTermStorageManager(file.DriveId);
            return await lts.HeaderFileExists(file.FileId);
        }

        public async Task SoftDeleteLongTermFile(InternalDriveFileId file)
        {
            await AssertCanWriteToDrive(file.DriveId);

            var existingHeader = await this.GetServerFileHeader(file);

            await WriteDeletedFileHeader(existingHeader);
        }

        public async Task HardDeleteLongTermFile(InternalDriveFileId file)
        {
            await AssertCanWriteToDrive(file.DriveId);

            var lts = await GetLongTermStorageManager(file.DriveId);
            await lts.HardDelete(file.FileId);

            if (await ShouldRaiseDriveEvent(file))
            {
                await mediator.Publish(new DriveFileDeletedNotification(ContextAccessor.GetCurrent())
                {
                    IsHardDelete = true,
                    File = file,
                    ServerFileHeader = null,
                    SharedSecretEncryptedFileHeader = null
                });
            }
        }

        public async Task CommitNewFile(InternalDriveFileId targetFile, KeyHeader keyHeader, FileMetadata metadata, ServerMetadata serverMetadata,
            bool? ignorePayload)
        {
            await AssertCanWriteToDrive(targetFile.DriveId);

            metadata.File = targetFile;
            serverMetadata.FileSystemType = GetFileSystemType();

            var storageManager = await GetLongTermStorageManager(targetFile.DriveId);
            var tempStorageManager = await GetTempStorageManager(targetFile.DriveId);

            //HACK: To the transit system sending the file header and not the payload or thumbnails (via SendContents)
            // ignorePayload and ignoreThumbnail allow it to tell us what to expect.

            bool metadataSaysThisFileHasPayloads = metadata.Payloads?.Any() ?? false;

            if (metadataSaysThisFileHasPayloads && !ignorePayload.GetValueOrDefault(false))
            {
                //TODO: update payload size to be a sum of all payloads
                foreach (var descriptor in metadata.Payloads)
                {
                    //Note: it's just as performant to directly get the file length as it is to perform File.Exists
                    var payloadExtension = DriveFileUtility.GetPayloadFileExtension(descriptor.Key, descriptor.Uid);
                    var sourceFile = await tempStorageManager.GetPath(targetFile.FileId, payloadExtension);
                    await storageManager.MovePayloadToLongTerm(targetFile.FileId, descriptor, sourceFile);

                    foreach (var thumb in descriptor.Thumbnails ?? new List<ThumbnailDescriptor>())
                    {
                        var extension = DriveFileUtility.GetThumbnailFileExtension(descriptor.Key, descriptor.Uid, thumb.PixelWidth,
                            thumb.PixelHeight);
                        var sourceThumbnail = await tempStorageManager.GetPath(targetFile.FileId, extension);
                        await storageManager.MoveThumbnailToLongTerm(targetFile.FileId, sourceThumbnail, descriptor, thumb);
                    }
                }
            }

            //TODO: calculate payload checksum, put on file metadata
            var serverHeader = await CreateServerHeaderInternal(targetFile, keyHeader, metadata, serverMetadata);

            await WriteNewFileHeader(targetFile, serverHeader);

            //clean up temp storage
            await tempStorageManager.EnsureDeleted(targetFile.FileId);

            if (await ShouldRaiseDriveEvent(targetFile))
            {
                await mediator.Publish(new DriveFileAddedNotification(ContextAccessor.GetCurrent())
                {
                    File = targetFile,
                    ServerFileHeader = serverHeader,
                });
            }
        }

        public async Task OverwriteFile(InternalDriveFileId tempFile, InternalDriveFileId targetFile, KeyHeader keyHeader, FileMetadata newMetadata,
            ServerMetadata serverMetadata, bool? ignorePayload)
        {
            await AssertCanWriteToDrive(targetFile.DriveId);

            var existingServerHeader = await this.GetServerFileHeader(targetFile);
            if (null == existingServerHeader)
            {
                throw new OdinClientException("Cannot overwrite file that does not exist", OdinClientErrorCode.FileNotFound);
            }

            if (existingServerHeader.FileMetadata.FileState != FileState.Active)
            {
                throw new OdinClientException("Cannot update a non-active file", OdinClientErrorCode.CannotUpdateNonActiveFile);
            }

            DriveFileUtility.AssertVersionTagMatch(existingServerHeader.FileMetadata.VersionTag, newMetadata.VersionTag);

            newMetadata.TransitCreated = existingServerHeader.FileMetadata.TransitCreated;
            newMetadata.TransitUpdated = existingServerHeader.FileMetadata.TransitUpdated;

            newMetadata.Created = existingServerHeader.FileMetadata.Created;

            //Only overwrite the globalTransitId if one is already set; otherwise let a file update set the ID (useful for mail-app drafts)
            if (existingServerHeader.FileMetadata.GlobalTransitId != null)
            {
                newMetadata.GlobalTransitId = existingServerHeader.FileMetadata.GlobalTransitId;
            }

            newMetadata.FileState = existingServerHeader.FileMetadata.FileState;
            newMetadata.ReactionPreview = existingServerHeader.FileMetadata.ReactionPreview;

            newMetadata.File = targetFile;
            //Note: our call to GetServerFileHeader earlier validates the existing
            serverMetadata.FileSystemType = existingServerHeader.ServerMetadata.FileSystemType;

            var longTermStorageManager = await GetLongTermStorageManager(targetFile.DriveId);
            var tempStorageManager = await GetTempStorageManager(tempFile.DriveId);

            //HACK: To support the transit system sending the file header and not the payload or thumbnails (via SendContents)
            // ignorePayload and ignoreThumbnail allow it to tell us what to expect.

            bool metadataSaysThisFileHasPayloads = newMetadata.Payloads?.Any() ?? false;

            await longTermStorageManager.DeleteMissingPayloads(newMetadata.File.FileId, newMetadata.Payloads);

            if (metadataSaysThisFileHasPayloads && !ignorePayload.GetValueOrDefault(false))
            {
                foreach (var descriptor in newMetadata.Payloads)
                {
                    //Note: it's just as performant to directly get the file length as it is to perform File.Exists
                    var payloadExtension = DriveFileUtility.GetPayloadFileExtension(descriptor.Key, descriptor.Uid);
                    var sourceFile = await tempStorageManager.GetPath(tempFile.FileId, payloadExtension);
                    await longTermStorageManager.MovePayloadToLongTerm(targetFile.FileId, descriptor, sourceFile);

                    // Process thumbnails
                    var thumbs = descriptor.Thumbnails;

                    await longTermStorageManager.DeleteMissingThumbnailFiles(targetFile.FileId, thumbs);
                    foreach (var thumb in thumbs)
                    {
                        var extension = DriveFileUtility.GetThumbnailFileExtension(descriptor.Key, descriptor.Uid, thumb.PixelWidth,
                            thumb.PixelHeight);
                        var sourceThumbnail = await tempStorageManager.GetPath(tempFile.FileId, extension);
                        await longTermStorageManager.MoveThumbnailToLongTerm(targetFile.FileId, sourceThumbnail, descriptor, thumb);
                    }
                }
            }

            var serverHeader = new ServerFileHeader()
            {
                EncryptedKeyHeader = await this.EncryptKeyHeader(tempFile.DriveId, keyHeader),
                FileMetadata = newMetadata,
                ServerMetadata = serverMetadata
            };

            await WriteFileHeaderInternal(serverHeader);

            //clean up temp storage
            await tempStorageManager.EnsureDeleted(targetFile.FileId);

            if (await ShouldRaiseDriveEvent(targetFile))
            {
                await mediator.Publish(new DriveFileChangedNotification(ContextAccessor.GetCurrent())
                {
                    File = targetFile,
                    ServerFileHeader = serverHeader,
                });
            }
        }

        public async Task<Guid> UpdatePayloads(
            InternalDriveFileId tempSourceFile,
            InternalDriveFileId targetFile,
            List<PayloadDescriptor> incomingPayloads)
        {
            await AssertCanWriteToDrive(targetFile.DriveId);

            var existingServerHeader = await this.GetServerFileHeader(targetFile);
            if (null == existingServerHeader)
            {
                throw new OdinClientException("Invalid target file", OdinClientErrorCode.FileNotFound);
            }

            if (existingServerHeader.FileMetadata.FileState != FileState.Active)
            {
                throw new OdinClientException("Cannot update a non-active file", OdinClientErrorCode.CannotUpdateNonActiveFile);
            }

            var storageManager = await GetLongTermStorageManager(targetFile.DriveId);
            var tempStorageManager = await GetTempStorageManager(tempSourceFile.DriveId);

            //Note: we do not delete existing payloads.  this feature adds or overwrites existing ones
            foreach (var descriptor in incomingPayloads)
            {
                // if the payload exists by key, overwrite; if not write new payload
                var payloadFileExtension = DriveFileUtility.GetPayloadFileExtension(descriptor.Key, descriptor.Uid);

                string sourceFilePath = await tempStorageManager.GetPath(tempSourceFile.FileId, payloadFileExtension);
                await storageManager.MovePayloadToLongTerm(targetFile.FileId, descriptor, sourceFilePath);

                // Delete any thumbnail that are no longer in the descriptor.Thumbnails from disk
                await storageManager.DeleteMissingThumbnailFiles(targetFile.FileId, descriptor.Thumbnails); //clean up

                foreach (var thumb in descriptor.Thumbnails ?? new List<ThumbnailDescriptor>())
                {
                    var extension = DriveFileUtility.GetThumbnailFileExtension(descriptor.Key, descriptor.Uid, thumb.PixelWidth,
                        thumb.PixelHeight);
                    var sourceThumbnail = await tempStorageManager.GetPath(tempSourceFile.FileId, extension);
                    await storageManager.MoveThumbnailToLongTerm(targetFile.FileId, sourceThumbnail, descriptor, thumb);
                }
            }

            List<PayloadDescriptor> finalPayloads = new List<PayloadDescriptor>();

            // Add the incoming list as the priority
            finalPayloads.AddRange(incomingPayloads);

            // Now Add any that were in the existing server header not already in the list
            var existingFiltered = existingServerHeader.FileMetadata.Payloads.Where(ep => incomingPayloads.All(ip => ip.Key != ep.Key));
            finalPayloads.AddRange(existingFiltered);

            existingServerHeader.FileMetadata.Payloads = finalPayloads;

            await WriteFileHeaderInternal(existingServerHeader);

            //clean up temp storage
            await tempStorageManager.EnsureDeleted(targetFile.FileId);

            if (await ShouldRaiseDriveEvent(targetFile))
            {
                await mediator.Publish(new DriveFileChangedNotification(ContextAccessor.GetCurrent())
                {
                    File = targetFile,
                    ServerFileHeader = existingServerHeader,
                });
            }

            return existingServerHeader.FileMetadata.VersionTag.GetValueOrDefault();
        }

        public async Task OverwriteMetadata(InternalDriveFileId targetFile, FileMetadata newMetadata, ServerMetadata newServerMetadata)
        {
            await AssertCanWriteToDrive(targetFile.DriveId);

            var existingServerHeader = await this.GetServerFileHeader(targetFile);
            if (null == existingServerHeader)
            {
                throw new OdinClientException("Cannot overwrite file that does not exist", OdinClientErrorCode.FileNotFound);
            }

            if (existingServerHeader.FileMetadata.FileState != FileState.Active)
            {
                throw new OdinClientException("Cannot update a non-active file", OdinClientErrorCode.CannotUpdateNonActiveFile);
            }

            DriveFileUtility.AssertVersionTagMatch(existingServerHeader.FileMetadata.VersionTag, newMetadata.VersionTag);

            newMetadata.File = targetFile;
            newMetadata.Created = existingServerHeader.FileMetadata.Created;
            newMetadata.GlobalTransitId = existingServerHeader.FileMetadata.GlobalTransitId;
            newMetadata.FileState = existingServerHeader.FileMetadata.FileState;
            newMetadata.Payloads = existingServerHeader.FileMetadata.Payloads;
            newMetadata.ReactionPreview = existingServerHeader.FileMetadata.ReactionPreview;

            newServerMetadata.FileSystemType = existingServerHeader.ServerMetadata.FileSystemType;

            existingServerHeader.FileMetadata = newMetadata;
            existingServerHeader.ServerMetadata = newServerMetadata;

            await WriteFileHeaderInternal(existingServerHeader);

            //clean up temp storage
            var tsm = await GetTempStorageManager(targetFile.DriveId);
            await tsm.EnsureDeleted(targetFile.FileId);

            if (await ShouldRaiseDriveEvent(targetFile))
            {
                await mediator.Publish(new DriveFileChangedNotification(ContextAccessor.GetCurrent())
                {
                    File = targetFile,
                    ServerFileHeader = existingServerHeader,
                });
            }
        }

        public async Task UpdateReactionPreview(InternalDriveFileId targetFile, ReactionSummary summary)
        {
            ContextAccessor.GetCurrent().PermissionsContext.AssertHasAtLeastOneDrivePermission(
                targetFile.DriveId, DrivePermission.React, DrivePermission.Comment);

            var lts = await GetLongTermStorageManager(targetFile.DriveId);
            var existingHeader = await lts.GetServerFileHeader(targetFile.FileId);
            existingHeader.FileMetadata.ReactionPreview = summary;
            await WriteFileHeaderInternal(existingHeader);

            //clean up temp storage
            var tsm = await GetTempStorageManager(targetFile.DriveId);
            await tsm.EnsureDeleted(targetFile.FileId);

            if (await ShouldRaiseDriveEvent(targetFile))
            {
                await mediator.Publish(new ReactionPreviewUpdatedNotification(ContextAccessor.GetCurrent())
                {
                    File = targetFile,
                    ServerFileHeader = existingHeader,
                    SharedSecretEncryptedFileHeader =
                        DriveFileUtility.ConvertToSharedSecretEncryptedClientFileHeader(existingHeader, ContextAccessor.GetCurrent())
                });
            }
        }

        // Start Feed drive hacks

        public async Task WriteNewFileToFeedDrive(KeyHeader keyHeader, FileMetadata fileMetadata)
        {
            // Method assumes you ensured the file was unique by some other method

            var feedDriveId = await DriveManager.GetDriveIdByAlias(SystemDriveConstants.FeedDrive);
            await AssertCanWriteToDrive(feedDriveId.GetValueOrDefault());
            var file = await this.CreateInternalFileId(feedDriveId.GetValueOrDefault());

            var serverMetadata = new ServerMetadata()
            {
                AccessControlList = AccessControlList.OwnerOnly,
                AllowDistribution = false
            };

            var serverFileHeader = await this.CreateServerFileHeader(file, keyHeader, fileMetadata, serverMetadata);
            await this.WriteNewFileHeader(file, serverFileHeader, raiseEvent: true);
        }

        public async Task ReplaceFileMetadataOnFeedDrive(InternalDriveFileId file, FileMetadata fileMetadata, bool bypassCallerCheck = false)
        {
            await AssertCanWriteToDrive(file.DriveId);
            var header = await GetServerFileHeaderInternal(file);
            AssertValidFileSystemType(header.ServerMetadata);
            var feedDriveId = await DriveManager.GetDriveIdByAlias(SystemDriveConstants.FeedDrive);

            if (file.DriveId != feedDriveId)
            {
                throw new OdinSystemException("Method cannot be used on drive");
            }

            if (!bypassCallerCheck) //eww
            {
                //S0510
                if (header.FileMetadata.SenderOdinId != ContextAccessor.GetCurrent().GetCallerOdinIdOrFail())
                {
                    throw new OdinSecurityException("Invalid caller");
                }
            }

            header.FileMetadata = fileMetadata;

            await this.UpdateActiveFileHeader(file, header, raiseEvent: true);
        }

        public async Task RemoveFeedDriveFile(InternalDriveFileId file)
        {
            await AssertCanWriteToDrive(file.DriveId);
            var header = await GetServerFileHeaderInternal(file);
            AssertValidFileSystemType(header.ServerMetadata);
            var feedDriveId = await DriveManager.GetDriveIdByAlias(SystemDriveConstants.FeedDrive);

            if (file.DriveId != feedDriveId)
            {
                throw new OdinSystemException("Method cannot be used on drive");
            }

            //S0510
            if (header.FileMetadata.SenderOdinId != ContextAccessor.GetCurrent().GetCallerOdinIdOrFail())
            {
                throw new OdinSecurityException("Invalid caller");
            }

            await WriteDeletedFileHeader(header);
        }

        public async Task UpdateReactionPreviewOnFeedDrive(InternalDriveFileId targetFile, ReactionSummary summary)
        {
            await AssertCanWriteToDrive(targetFile.DriveId);
            var feedDriveId = await DriveManager.GetDriveIdByAlias(SystemDriveConstants.FeedDrive);
            if (targetFile.DriveId != feedDriveId)
            {
                throw new OdinSystemException("Cannot update reaction preview on this drive");
            }

            var lts = await GetLongTermStorageManager(targetFile.DriveId);
            var existingHeader = await lts.GetServerFileHeader(targetFile.FileId);

            //S0510
            if (existingHeader.FileMetadata.SenderOdinId != ContextAccessor.GetCurrent().Caller.OdinId)
            {
                throw new OdinSecurityException("Invalid caller");
            }

            existingHeader.FileMetadata.ReactionPreview = summary;
            await WriteFileHeaderInternal(existingHeader);

            //clean up temp storage
            var tsm = await GetTempStorageManager(targetFile.DriveId);
            await tsm.EnsureDeleted(targetFile.FileId);

            if (await ShouldRaiseDriveEvent(targetFile))
            {
                await mediator.Publish(new ReactionPreviewUpdatedNotification(ContextAccessor.GetCurrent())
                {
                    File = targetFile,
                    ServerFileHeader = existingHeader,
                    SharedSecretEncryptedFileHeader =
                        DriveFileUtility.ConvertToSharedSecretEncryptedClientFileHeader(existingHeader, ContextAccessor.GetCurrent())
                });
            }
        }

        // End feed drive hacks

        private async Task<LongTermStorageManager> GetLongTermStorageManager(Guid driveId)
        {
            var logger = loggerFactory.CreateLogger<LongTermStorageManager>();
            var drive = await DriveManager.GetDrive(driveId, failIfInvalid: true);
            var manager = new LongTermStorageManager(drive, logger, driveFileReaderWriter);
            return manager;
        }

        private async Task<TempStorageManager> GetTempStorageManager(Guid driveId)
        {
            var drive = await DriveManager.GetDrive(driveId, failIfInvalid: true);
            var logger = loggerFactory.CreateLogger<TempStorageManager>();
            return new TempStorageManager(drive, driveFileReaderWriter, logger);
        }

        private async Task WriteFileHeaderInternal(ServerFileHeader header, bool keepSameVersionTag = false)
        {
            if (!keepSameVersionTag)
            {
                header.FileMetadata.VersionTag = SequentialGuid.CreateGuid();
            }

            header.FileMetadata.Updated = UnixTimeUtc.Now().milliseconds;

            var file = header.FileMetadata.File;
            var lts = await GetLongTermStorageManager(file.DriveId);
            var payloadDiskUsage = await lts.GetPayloadDiskUsage(file.FileId);

            var json = OdinSystemSerializer.Serialize(header);
            header.ServerMetadata.FileByteCount = payloadDiskUsage + Encoding.UTF8.GetBytes(json).Length;

            json = OdinSystemSerializer.Serialize(header);
            var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));

            var mgr = await GetLongTermStorageManager(header.FileMetadata.File.DriveId);

            //Note: this can probably be removed because the underlying file writer has retries in it
            var attempts = await TryRetry.WithDelayAsync(
                odinConfiguration.Host.FileOperationRetryAttempts,
                odinConfiguration.Host.FileOperationRetryDelayMs,
                CancellationToken.None,
                async () => await mgr.WriteHeaderStream(header.FileMetadata.File.FileId, stream));

            if (_logger.IsEnabled(LogLevel.Trace) && attempts > 1)
            {
                _logger.LogTrace("It took {attempts} attempts to write file [{file}] on driveId [{driveId}]", attempts, file.FileId, file.DriveId);
            }
        }

        /// <summary>
        /// Protects against using the wrong filesystem with a fileId
        /// </summary>
        /// <param name="serverMetadata"></param>
        /// <exception cref="OdinClientException"></exception>
        private void AssertValidFileSystemType(ServerMetadata serverMetadata)
        {
            if (serverMetadata.FileSystemType != GetFileSystemType())
            {
                //just in case the caller used the wrong drive service instance
                throw new OdinClientException($"Invalid SystemFileCategory.  This service only handles the FileSystemType of {GetFileSystemType()}");
            }
        }

        private async Task<bool> ShouldRaiseDriveEvent(InternalDriveFileId file)
        {
            return file.DriveId != (await DriveManager.GetDriveIdByAlias(SystemDriveConstants.TransientTempDrive));
        }

        private async Task WriteDeletedFileHeader(ServerFileHeader existingHeader)
        {
            var file = existingHeader.FileMetadata.File;

            var deletedServerFileHeader = new ServerFileHeader()
            {
                EncryptedKeyHeader = existingHeader.EncryptedKeyHeader,
                FileMetadata = new FileMetadata(existingHeader.FileMetadata.File)
                {
                    FileState = FileState.Deleted,
                    Updated = UnixTimeUtc.Now().milliseconds,
                    GlobalTransitId = existingHeader.FileMetadata.GlobalTransitId
                },
                ServerMetadata = existingHeader.ServerMetadata
            };

            var lts = await GetLongTermStorageManager(file.DriveId);
            await lts.DeleteAttachments(file.FileId);
            await this.WriteFileHeaderInternal(deletedServerFileHeader);

            if (await ShouldRaiseDriveEvent(file))
            {
                await mediator.Publish(new DriveFileDeletedNotification(ContextAccessor.GetCurrent())
                {
                    PreviousServerFileHeader = existingHeader,
                    IsHardDelete = false,
                    File = file,
                    ServerFileHeader = deletedServerFileHeader,
                    SharedSecretEncryptedFileHeader =
                        DriveFileUtility.ConvertToSharedSecretEncryptedClientFileHeader(deletedServerFileHeader, ContextAccessor.GetCurrent())
                });
            }
        }

        private async Task<ServerFileHeader> CreateServerHeaderInternal(InternalDriveFileId targetFile, KeyHeader keyHeader, FileMetadata metadata,
            ServerMetadata serverMetadata)
        {
            serverMetadata.FileSystemType = GetFileSystemType();

            return new ServerFileHeader()
            {
                EncryptedKeyHeader = metadata.IsEncrypted ? await this.EncryptKeyHeader(targetFile.DriveId, keyHeader) : EncryptedKeyHeader.Empty(),
                FileMetadata = metadata,
                ServerMetadata = serverMetadata
            };
        }

        private async Task<ServerFileHeader> GetServerFileHeaderInternal(InternalDriveFileId file)
        {
            var mgr = await GetLongTermStorageManager(file.DriveId);

            ServerFileHeader header = null;
            var attempts = await TryRetry.WithDelayAsync(
                odinConfiguration.Host.FileOperationRetryAttempts,
                odinConfiguration.Host.FileOperationRetryDelayMs,
                CancellationToken.None,
                async () =>
                {
                    header = await mgr.GetServerFileHeader(file.FileId);
                });

            if (_logger.IsEnabled(LogLevel.Trace) && attempts > 1)
            {
                _logger.LogTrace("It took {attempts} attempts to read file [{file}] on driveId [{driveId}]", attempts, file.FileId, file.DriveId);
            }

            if (null == header)
            {
                return null;
            }

            await driveAclAuthorizationService.AssertCallerHasPermission(header.ServerMetadata.AccessControlList);

            return header;
        }

        private async Task UpdateActiveFileHeaderInternal(InternalDriveFileId targetFile, ServerFileHeader header, bool keepSameVersionTag,
            bool raiseEvent = false)
        {
            if (!header.IsValid())
            {
                throw new OdinSystemException("An invalid header was passed to the update header method.  You need more checks in place before getting here");
            }

            await AssertCanWriteToDrive(targetFile.DriveId);

            //short circuit
            var fileExists = await FileExists(targetFile);
            if (!fileExists)
            {
                await WriteNewFileHeader(targetFile, header, raiseEvent);
                return;
            }

            var metadata = header.FileMetadata;

            //TODO: need to encrypt the metadata parts
            metadata.File = targetFile; //TBH it's strange having this but we need the metadata to have the file and drive embedded

            if (metadata.FileState != FileState.Active)
            {
                throw new OdinClientException("Cannot update non-active file", OdinClientErrorCode.CannotUpdateNonActiveFile);
            }

            var existingHeader = await this.GetServerFileHeaderInternal(targetFile);
            metadata.Created = existingHeader.FileMetadata.Created;
            metadata.GlobalTransitId = existingHeader.FileMetadata.GlobalTransitId;
            metadata.FileState = existingHeader.FileMetadata.FileState;
            metadata.SenderOdinId = existingHeader.FileMetadata.SenderOdinId;

            await WriteFileHeaderInternal(header, keepSameVersionTag);

            //clean up temp storage
            var tsm = await GetTempStorageManager(targetFile.DriveId);
            await tsm.EnsureDeleted(targetFile.FileId);

            //HACKed in for Feed drive
            if (raiseEvent)
            {
                if (await ShouldRaiseDriveEvent(targetFile))
                {
                    await mediator.Publish(new DriveFileChangedNotification(ContextAccessor.GetCurrent())
                    {
                        File = targetFile,
                        ServerFileHeader = header,
                    });
                }
            }
        }
    }
}