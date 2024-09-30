using System;
using System.Collections.Generic;
using System.Diagnostics;
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
using Odin.Core.Storage.SQLite;
using Odin.Core.Time;
using Odin.Core.Util;
using Odin.Services.Apps;
using Odin.Services.Authorization.Acl;
using Odin.Services.Base;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.FileSystem.Base.Upload;
using Odin.Services.Drives.Management;
using Odin.Services.Mediator;
using Odin.Services.Peer.Encryption;
using Odin.Services.Util;

namespace Odin.Services.Drives.FileSystem.Base
{
    public abstract class DriveStorageServiceBase(
        ILoggerFactory loggerFactory,
        IMediator mediator,
        IDriveAclAuthorizationService driveAclAuthorizationService,
        DriveManager driveManager,
        ConcurrentFileManager concurrentFileManager,
        DriveFileReaderWriter driveFileReaderWriter) : RequirePermissionsBase
    {
        private readonly ILogger<DriveStorageServiceBase> _logger = loggerFactory.CreateLogger<DriveStorageServiceBase>();

        protected override DriveManager DriveManager { get; } = driveManager;

        /// <summary>
        /// Gets the <see cref="FileSystemType"/> of which the inheriting class manages
        /// </summary>
        public abstract FileSystemType GetFileSystemType();

        public async Task<SharedSecretEncryptedFileHeader> GetSharedSecretEncryptedHeader(InternalDriveFileId file, IOdinContext odinContext,
            DatabaseConnection cn)
        {
            var serverFileHeader = await this.GetServerFileHeader(file, odinContext, cn);
            if (serverFileHeader == null)
            {
                return null;
            }

            var result = DriveFileUtility.CreateClientFileHeader(serverFileHeader, odinContext);
            return result;
        }

        /// <summary>
        /// Gets an EncryptedKeyHeader for a given payload using the payload's IV
        /// </summary>
        public async Task<(ServerFileHeader header, PayloadDescriptor payloadDescriptor, EncryptedKeyHeader encryptedKeyHeader, bool fileExists)>
            GetPayloadSharedSecretEncryptedKeyHeader(InternalDriveFileId file, string payloadKey, IOdinContext odinContext, DatabaseConnection cn)
        {
            var serverFileHeader = await this.GetServerFileHeader(file, odinContext, cn);
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
                odinContext);

            return (serverFileHeader, payloadDescriptor, payloadEncryptedKeyHeader, true);
        }

        public async Task<InternalDriveFileId> CreateInternalFileId(Guid driveId, DatabaseConnection cn)
        {
            var lts = await GetLongTermStorageManager(driveId, cn);
            var df = new InternalDriveFileId()
            {
                FileId = lts.CreateFileId(),
                DriveId = driveId,
            };

            return df;
        }

        public async Task UpdateActiveFileHeaderInternal(InternalDriveFileId targetFile, ServerFileHeader header, bool keepSameVersionTag,
            IOdinContext odinContext, DatabaseConnection cn,
            bool raiseEvent = false, bool ignoreFeedDistribution = false)
        {
            if (!header.IsValid())
            {
                throw new OdinSystemException("An invalid header was passed to the update header method.  You need more checks in place before getting here");
            }

            await AssertCanWriteToDrive(targetFile.DriveId, odinContext, cn);

            //short circuit
            var fileExists = await FileExists(targetFile, odinContext, cn);
            if (!fileExists)
            {
                await WriteNewFileHeader(targetFile, header, odinContext, cn, raiseEvent);
                return;
            }

            var metadata = header.FileMetadata;

            //TODO: need to encrypt the metadata parts
            metadata.File = targetFile; //TBH it's strange having this but we need the metadata to have the file and drive embedded

            if (metadata.FileState != FileState.Active)
            {
                throw new OdinClientException("Cannot update non-active file", OdinClientErrorCode.CannotUpdateNonActiveFile);
            }

            var existingHeader = await this.GetServerFileHeaderInternal(targetFile, odinContext, cn);
            metadata.Created = existingHeader.FileMetadata.Created;
            metadata.GlobalTransitId = existingHeader.FileMetadata.GlobalTransitId;
            metadata.FileState = existingHeader.FileMetadata.FileState;
            metadata.SenderOdinId = existingHeader.FileMetadata.SenderOdinId;
            metadata.OriginalAuthor = existingHeader.FileMetadata.OriginalAuthor;

            await WriteFileHeaderInternal(header, cn, keepSameVersionTag);

            //clean up temp storage
            var tsm = await GetTempStorageManager(targetFile.DriveId, cn);
            await tsm.EnsureDeleted(targetFile.FileId);

            //HACKed in for Feed drive
            if (raiseEvent)
            {
                if (await ShouldRaiseDriveEvent(targetFile, cn))
                {
                    await mediator.Publish(new DriveFileChangedNotification
                    {
                        File = targetFile,
                        ServerFileHeader = header,
                        OdinContext = odinContext,
                        DatabaseConnection = cn,
                        IgnoreFeedDistribution = ignoreFeedDistribution
                    });
                }
            }
        }

        /// <summary>
        /// Writes a new file header w/o checking for an existing one
        /// </summary>
        public async Task WriteNewFileHeader(InternalDriveFileId targetFile, ServerFileHeader header, IOdinContext odinContext, DatabaseConnection cn,
            bool raiseEvent = false)
        {
            if (!header.IsValid())
            {
                throw new OdinSystemException("An invalid header was passed to the update header method.  You need more checks in place before getting here");
            }

            await AssertCanWriteToDrive(targetFile.DriveId, odinContext, cn);

            var metadata = header.FileMetadata;

            //TODO: need to encrypt the metadata parts
            metadata.File = targetFile; //TBH it's strange having this but we need the metadata to have the file and drive embedded
            metadata.Created = header.FileMetadata.Created != 0 ? header.FileMetadata.Created : UnixTimeUtc.Now().milliseconds;
            metadata.FileState = FileState.Active;

            await WriteFileHeaderInternal(header, cn);

            //clean up temp storage
            var tsm = await GetTempStorageManager(targetFile.DriveId, cn);
            await tsm.EnsureDeleted(targetFile.FileId);

            //HACKed in for Feed drive
            if (raiseEvent)
            {
                if (await ShouldRaiseDriveEvent(targetFile, cn))
                {
                    await mediator.Publish(new DriveFileAddedNotification
                    {
                        File = targetFile,
                        ServerFileHeader = header,
                        OdinContext = odinContext,
                        DatabaseConnection = cn
                    });
                }
            }
        }

        public async Task<uint> WriteTempStream(InternalDriveFileId file, string extension, Stream stream, IOdinContext odinContext, DatabaseConnection cn)
        {
            await AssertCanWriteToDrive(file.DriveId, odinContext, cn);
            var tsm = await GetTempStorageManager(file.DriveId, cn);
            return await tsm.WriteStream(file.FileId, extension, stream);
        }

        /// <summary>
        /// Reads the whole file so be sure this is only used on small'ish files; ones you're ok with loaded fully into server-memory
        /// </summary>
        /// <returns></returns>
        public async Task<byte[]> GetAllFileBytesFromTemp(InternalDriveFileId file, string extension, IOdinContext odinContext, DatabaseConnection cn)
        {
            await this.AssertCanReadDrive(file.DriveId, odinContext, cn);
            var tsm = await GetTempStorageManager(file.DriveId, cn);
            var bytes = await tsm.GetAllFileBytes(file.FileId, extension);
            return bytes;
        }

        public async Task<byte[]> GetAllFileBytesForWriting(InternalDriveFileId file, string extension, IOdinContext odinContext, DatabaseConnection cn)
        {
            await AssertCanWriteToDrive(file.DriveId, odinContext, cn);
            var tsm = await GetTempStorageManager(file.DriveId, cn);
            return await tsm.GetAllFileBytes(file.FileId, extension);
        }

        public async Task DeleteTempFile(InternalDriveFileId file, string extension, IOdinContext odinContext, DatabaseConnection cn)
        {
            await AssertCanWriteToDrive(file.DriveId, odinContext, cn);
            var tsm = await GetTempStorageManager(file.DriveId, cn);
            await tsm.EnsureDeleted(file.FileId, extension);
        }

        public async Task DeleteTempFiles(InternalDriveFileId file, IOdinContext odinContext, DatabaseConnection cn)
        {
            await AssertCanWriteToDrive(file.DriveId, odinContext, cn);

            var tsm = await GetTempStorageManager(file.DriveId, cn);
            await tsm.EnsureDeleted(file.FileId);
        }

        public async Task<(Stream stream, ThumbnailDescriptor thumbnail)> GetThumbnailPayloadStream(InternalDriveFileId file, int width, int height,
            string payloadKey, UnixTimeUtcUnique payloadUid, IOdinContext odinContext, DatabaseConnection cn, bool directMatchOnly = false)
        {
            await AssertCanReadDrive(file.DriveId, odinContext, cn);

            DriveFileUtility.AssertValidPayloadKey(payloadKey);
            var lts = await GetLongTermStorageManager(file.DriveId, cn);

            //Note: calling to get the file header so we can ensure the caller can read this file
            var header = await this.GetServerFileHeader(file, odinContext, cn);
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
                    var drive = await DriveManager.GetDrive(file.DriveId, cn);
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
                var drive = await DriveManager.GetDrive(file.DriveId, cn);
                if (drive.TargetDriveInfo == SystemDriveConstants.FeedDrive)
                {
                    return (Stream.Null, nextSizeUp);
                }

                throw;
            }
        }


        public async Task<Guid> DeletePayload(InternalDriveFileId file, string key, Guid targetVersionTag, IOdinContext odinContext, DatabaseConnection cn)
        {
            await AssertCanWriteToDrive(file.DriveId, odinContext, cn);

            //Note: calling to get the file header so we can ensure the caller can read this file
            var header = await this.GetServerFileHeader(file, odinContext, cn);
            DriveFileUtility.AssertVersionTagMatch(header.FileMetadata.VersionTag, targetVersionTag);

            var descriptorIndex = header.FileMetadata.Payloads?.FindIndex(p => string.Equals(p.Key, key, StringComparison.InvariantCultureIgnoreCase)) ?? -1;

            if (descriptorIndex == -1)
            {
                return Guid.Empty;
            }

            var descriptor = header.FileMetadata.Payloads![descriptorIndex];

            await DeletePayloadFromDiskInternal(file, descriptor, cn);

            header.FileMetadata.Payloads!.RemoveAt(descriptorIndex);
            await UpdateActiveFileHeader(file, header, odinContext, cn);
            return header.FileMetadata.VersionTag.GetValueOrDefault(); // this works because because pass header all the way
        }

        public async Task<ServerFileHeader> CreateServerFileHeader(InternalDriveFileId file, KeyHeader keyHeader, FileMetadata metadata,
            ServerMetadata serverMetadata, IOdinContext odinContext, DatabaseConnection cn)
        {
            return await CreateServerHeaderInternal(file, keyHeader, metadata, serverMetadata, odinContext, cn);
        }

        private async Task<EncryptedKeyHeader> EncryptKeyHeader(Guid driveId, KeyHeader keyHeader, IOdinContext odinContext, DatabaseConnection cn)
        {
            var storageKey = odinContext.PermissionsContext.GetDriveStorageKey(driveId);

            (await this.DriveManager.GetDrive(driveId, cn)).AssertValidStorageKey(storageKey);

            var encryptedKeyHeader = EncryptedKeyHeader.EncryptKeyHeaderAes(keyHeader, keyHeader.Iv, ref storageKey);
            return encryptedKeyHeader;
        }

        public async Task<bool> CallerHasPermissionToFile(InternalDriveFileId file, IOdinContext odinContext, DatabaseConnection cn)
        {
            var lts = await GetLongTermStorageManager(file.DriveId, cn);
            var header = await lts.GetServerFileHeader(file.FileId);

            if (null == header)
            {
                return false;
            }

            return await driveAclAuthorizationService.CallerHasPermission(header.ServerMetadata.AccessControlList, odinContext);
        }

        public async Task<ServerFileHeader> GetServerFileHeader(InternalDriveFileId file, IOdinContext odinContext, DatabaseConnection cn)
        {
            await AssertCanReadDrive(file.DriveId, odinContext, cn);
            var header = await GetServerFileHeaderInternal(file, odinContext, cn);

            if (header == null)
            {
                return null;
            }

            AssertValidFileSystemType(header.ServerMetadata);
            return header;
        }

        public async Task<ServerFileHeader> GetServerFileHeaderForWriting(InternalDriveFileId file, IOdinContext odinContext, DatabaseConnection cn)
        {
            await AssertCanWriteToDrive(file.DriveId, odinContext, cn);
            var header = await GetServerFileHeaderInternal(file, odinContext, cn);

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
        public async Task<FileSystemType> ResolveFileSystemType(InternalDriveFileId file, IOdinContext odinContext, DatabaseConnection cn)
        {
            await AssertCanReadOrWriteToDrive(file.DriveId, odinContext, cn);

            var header = await GetServerFileHeaderInternal(file, odinContext, cn);
            return header.ServerMetadata.FileSystemType;
        }

        public async Task<PayloadStream> GetPayloadStream(InternalDriveFileId file, string key, FileChunk chunk, IOdinContext odinContext,
            DatabaseConnection cn)
        {
            await AssertCanReadDrive(file.DriveId, odinContext, cn);
            DriveFileUtility.AssertValidPayloadKey(key);

            //Note: calling to get the file header will also
            //ensure the caller can touch this file.
            var header = await GetServerFileHeader(file, odinContext, cn);
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
                var lts = await GetLongTermStorageManager(file.DriveId, cn);
                var stream = await lts.GetPayloadStream(file.FileId, descriptor, chunk);
                return new PayloadStream(descriptor, stream.Length, stream);
            }
            catch (OdinFileHeaderHasCorruptPayloadException)
            {
                var drive = await DriveManager.GetDrive(file.DriveId, cn);
                if (drive.TargetDriveInfo == SystemDriveConstants.FeedDrive)
                {
                    return null;
                }

                throw;
            }
        }

        public async Task<bool> FileExists(InternalDriveFileId file, IOdinContext odinContext, DatabaseConnection cn)
        {
            await AssertCanReadOrWriteToDrive(file.DriveId, odinContext, cn);
            var lts = await GetLongTermStorageManager(file.DriveId, cn);
            return await lts.HeaderFileExists(file.FileId);
        }

        public async Task SoftDeleteLongTermFile(InternalDriveFileId file, IOdinContext odinContext, DatabaseConnection cn)
        {
            await AssertCanWriteToDrive(file.DriveId, odinContext, cn);

            var existingHeader = await this.GetServerFileHeaderInternal(file, odinContext, cn);

            await WriteDeletedFileHeader(existingHeader, odinContext, cn);
        }

        public async Task HardDeleteLongTermFile(InternalDriveFileId file, IOdinContext odinContext, DatabaseConnection cn)
        {
            await AssertCanWriteToDrive(file.DriveId, odinContext, cn);

            var lts = await GetLongTermStorageManager(file.DriveId, cn);
            await lts.HardDelete(file.FileId);

            if (await ShouldRaiseDriveEvent(file, cn))
            {
                await mediator.Publish(new DriveFileDeletedNotification
                {
                    IsHardDelete = true,
                    File = file,
                    ServerFileHeader = null,
                    SharedSecretEncryptedFileHeader = null,
                    OdinContext = odinContext,
                    DatabaseConnection = cn
                });
            }
        }

        public async Task CommitNewFile(InternalDriveFileId targetFile, KeyHeader keyHeader, FileMetadata metadata, ServerMetadata serverMetadata,
            bool? ignorePayload, IOdinContext odinContext, DatabaseConnection cn)
        {
            await AssertCanWriteToDrive(targetFile.DriveId, odinContext, cn);

            metadata.File = targetFile;
            serverMetadata.FileSystemType = GetFileSystemType();

            var storageManager = await GetLongTermStorageManager(targetFile.DriveId, cn);
            var tempStorageManager = await GetTempStorageManager(targetFile.DriveId, cn);

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
            var serverHeader = await CreateServerHeaderInternal(targetFile, keyHeader, metadata, serverMetadata, odinContext, cn);

            await WriteNewFileHeader(targetFile, serverHeader, odinContext, cn);

            //clean up temp storage
            await tempStorageManager.EnsureDeleted(targetFile.FileId);

            if (await ShouldRaiseDriveEvent(targetFile, cn))
            {
                await mediator.Publish(new DriveFileAddedNotification
                {
                    File = targetFile,
                    ServerFileHeader = serverHeader,
                    OdinContext = odinContext,
                    DatabaseConnection = cn
                });
            }
        }

        public async Task OverwriteFile(InternalDriveFileId tempFile, InternalDriveFileId targetFile, KeyHeader keyHeader, FileMetadata newMetadata,
            ServerMetadata serverMetadata, bool? ignorePayload, IOdinContext odinContext, DatabaseConnection cn)
        {
            await AssertCanWriteToDrive(targetFile.DriveId, odinContext, cn);

            var existingServerHeader = await this.GetServerFileHeader(targetFile, odinContext, cn);
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

            var longTermStorageManager = await GetLongTermStorageManager(targetFile.DriveId, cn);
            var tempStorageManager = await GetTempStorageManager(tempFile.DriveId, cn);

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
                EncryptedKeyHeader = await this.EncryptKeyHeader(tempFile.DriveId, keyHeader, odinContext, cn),
                FileMetadata = newMetadata,
                ServerMetadata = serverMetadata
            };

            await WriteFileHeaderInternal(serverHeader, cn);

            //clean up temp storage
            await tempStorageManager.EnsureDeleted(targetFile.FileId);

            if (await ShouldRaiseDriveEvent(targetFile, cn))
            {
                await mediator.Publish(new DriveFileChangedNotification
                {
                    File = targetFile,
                    ServerFileHeader = serverHeader,
                    OdinContext = odinContext,
                    DatabaseConnection = cn
                });
            }
        }

        public async Task<Guid> UpdatePayloads(
            InternalDriveFileId tempSourceFile,
            InternalDriveFileId targetFile,
            List<PayloadDescriptor> incomingPayloads,
            IOdinContext odinContext,
            DatabaseConnection cn)
        {
            await AssertCanWriteToDrive(targetFile.DriveId, odinContext, cn);

            var existingServerHeader = await this.GetServerFileHeader(targetFile, odinContext, cn);
            if (null == existingServerHeader)
            {
                throw new OdinClientException("Invalid target file", OdinClientErrorCode.FileNotFound);
            }

            if (existingServerHeader.FileMetadata.FileState != FileState.Active)
            {
                throw new OdinClientException("Cannot update a non-active file", OdinClientErrorCode.CannotUpdateNonActiveFile);
            }

            var storageManager = await GetLongTermStorageManager(targetFile.DriveId, cn);
            var tempStorageManager = await GetTempStorageManager(tempSourceFile.DriveId, cn);

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

            await WriteFileHeaderInternal(existingServerHeader, cn);

            //clean up temp storage
            await tempStorageManager.EnsureDeleted(targetFile.FileId);

            if (await ShouldRaiseDriveEvent(targetFile, cn))
            {
                await mediator.Publish(new DriveFileChangedNotification
                {
                    File = targetFile,
                    ServerFileHeader = existingServerHeader,
                    OdinContext = odinContext,
                    DatabaseConnection = cn
                });
            }

            return existingServerHeader.FileMetadata.VersionTag.GetValueOrDefault();
        }

        public async Task OverwriteMetadata(byte[] newKeyHeaderIv, InternalDriveFileId targetFile, FileMetadata newMetadata, ServerMetadata newServerMetadata,
            IOdinContext odinContext, DatabaseConnection cn)
        {
            await AssertCanWriteToDrive(targetFile.DriveId, odinContext, cn);

            var existingServerHeader = await this.GetServerFileHeader(targetFile, odinContext, cn);

            await OverwriteMetadataInternal(newKeyHeaderIv, existingServerHeader, newMetadata, newServerMetadata, odinContext, cn);

            //clean up temp storage
            var tsm = await GetTempStorageManager(targetFile.DriveId, cn);
            await tsm.EnsureDeleted(targetFile.FileId);

            if (await ShouldRaiseDriveEvent(targetFile, cn))
            {
                await mediator.Publish(new DriveFileChangedNotification
                {
                    File = targetFile,
                    ServerFileHeader = existingServerHeader,
                    OdinContext = odinContext,
                    DatabaseConnection = cn
                });
            }
        }

        public async Task UpdateReactionPreview(InternalDriveFileId targetFile, ReactionSummary summary, IOdinContext odinContext, DatabaseConnection cn)
        {
            odinContext.PermissionsContext.AssertHasAtLeastOneDrivePermission(
                targetFile.DriveId, DrivePermission.React, DrivePermission.Comment, DrivePermission.Write);
            var lts = await GetLongTermStorageManager(targetFile.DriveId, cn);
            var existingHeader = await lts.GetServerFileHeader(targetFile.FileId);
            existingHeader.FileMetadata.ReactionPreview = summary;
            await WriteFileHeaderInternal(existingHeader, cn, keepSameVersionTag: true);

            //clean up temp storage
            var tsm = await GetTempStorageManager(targetFile.DriveId, cn);
            await tsm.EnsureDeleted(targetFile.FileId);

            if (await ShouldRaiseDriveEvent(targetFile, cn))
            {
                await mediator.Publish(new ReactionPreviewUpdatedNotification
                {
                    File = targetFile,
                    ServerFileHeader = existingHeader,
                    SharedSecretEncryptedFileHeader = DriveFileUtility.CreateClientFileHeader(existingHeader, odinContext),
                    OdinContext = odinContext,
                    DatabaseConnection = cn
                });
            }
        }

        // Feed drive hacks

        public async Task WriteNewFileToFeedDrive(KeyHeader keyHeader, FileMetadata fileMetadata, IOdinContext odinContext, DatabaseConnection cn)
        {
            // Method assumes you ensured the file was unique by some other method

            var feedDriveId = await DriveManager.GetDriveIdByAlias(SystemDriveConstants.FeedDrive, cn);
            await AssertCanWriteToDrive(feedDriveId.GetValueOrDefault(), odinContext, cn);
            var file = await this.CreateInternalFileId(feedDriveId.GetValueOrDefault(), cn);

            var serverMetadata = new ServerMetadata()
            {
                AccessControlList = AccessControlList.OwnerOnly,
                AllowDistribution = false
            };

            //we don't accept uniqueIds into the feed
            fileMetadata.AppData.UniqueId = null;

            var serverFileHeader = await this.CreateServerFileHeader(file, keyHeader, fileMetadata, serverMetadata, odinContext, cn);
            await this.WriteNewFileHeader(file, serverFileHeader, odinContext, cn, raiseEvent: true);
        }

        public async Task ReplaceFileMetadataOnFeedDrive(InternalDriveFileId file, FileMetadata fileMetadata, IOdinContext odinContext, DatabaseConnection cn,
            bool bypassCallerCheck = false)
        {
            await AssertCanWriteToDrive(file.DriveId, odinContext, cn);
            var header = await GetServerFileHeaderInternal(file, odinContext, cn);
            AssertValidFileSystemType(header.ServerMetadata);

            if (header == null)
            {
                throw new OdinClientException("Trying to update feed metadata for non-existent file", OdinClientErrorCode.InvalidFile);
            }

            if (header.FileMetadata.FileState == FileState.Deleted)
            {
                // _logger.LogDebug("ReplaceFileMetadataOnFeedDrive - attempted to update a deleted file; this will be ignored.");
                return;
            }

            AssertValidFileSystemType(header.ServerMetadata);

            var feedDriveId = await DriveManager.GetDriveIdByAlias(SystemDriveConstants.FeedDrive, cn);
            if (file.DriveId != feedDriveId)
            {
                throw new OdinSystemException("Method cannot be used on drive");
            }

            if (!bypassCallerCheck) //eww: this allows the follower service to synchronize files when you start following someone.
            {
                //S0510
                if (header.FileMetadata.SenderOdinId != odinContext.GetCallerOdinIdOrFail())
                {
                    _logger.LogDebug("ReplaceFileMetadataOnFeedDrive - header file sender ({sender}) did not match context sender {ctx}",
                        header.FileMetadata.SenderOdinId,
                        odinContext.GetCallerOdinIdOrFail());
                    throw new OdinSecurityException("Invalid caller");
                }
            }

            header.FileMetadata = fileMetadata;

            // Clearing the UID for any files that go into the feed drive because the feed drive 
            // comes from multiple channel drives from many different identities so there could be a clash
            header.FileMetadata.AppData.UniqueId = null;

            await this.UpdateActiveFileHeader(file, header, odinContext, cn, raiseEvent: true);
        }

        public async Task RemoveFeedDriveFile(InternalDriveFileId file, IOdinContext odinContext, DatabaseConnection cn)
        {
            await AssertCanWriteToDrive(file.DriveId, odinContext, cn);
            var header = await GetServerFileHeaderInternal(file, odinContext, cn);
            AssertValidFileSystemType(header.ServerMetadata);
            var feedDriveId = await DriveManager.GetDriveIdByAlias(SystemDriveConstants.FeedDrive, cn);

            if (file.DriveId != feedDriveId)
            {
                throw new OdinSystemException("Method cannot be used on drive");
            }

            //S0510
            if (header.FileMetadata.SenderOdinId != odinContext.GetCallerOdinIdOrFail())
            {
                throw new OdinSecurityException("Invalid caller");
            }

            await WriteDeletedFileHeader(header, odinContext, cn);
        }

        public async Task UpdateReactionPreviewOnFeedDrive(InternalDriveFileId targetFile, ReactionSummary summary, IOdinContext odinContext,
            DatabaseConnection cn)
        {
            await AssertCanWriteToDrive(targetFile.DriveId, odinContext, cn);
            var feedDriveId = await DriveManager.GetDriveIdByAlias(SystemDriveConstants.FeedDrive, cn);
            if (targetFile.DriveId != feedDriveId)
            {
                throw new OdinSystemException("Cannot update reaction preview on this drive");
            }

            var lts = await GetLongTermStorageManager(targetFile.DriveId, cn);
            var existingHeader = await lts.GetServerFileHeader(targetFile.FileId);

            //S0510
            if (existingHeader.FileMetadata.SenderOdinId != odinContext.Caller.OdinId)
            {
                throw new OdinSecurityException("Invalid caller");
            }

            existingHeader.FileMetadata.ReactionPreview = summary;
            await WriteFileHeaderInternal(existingHeader, cn);

            //clean up temp storage
            var tsm = await GetTempStorageManager(targetFile.DriveId, cn);
            await tsm.EnsureDeleted(targetFile.FileId);

            if (await ShouldRaiseDriveEvent(targetFile, cn))
            {
                await mediator.Publish(new ReactionPreviewUpdatedNotification
                {
                    File = targetFile,
                    ServerFileHeader = existingHeader,
                    SharedSecretEncryptedFileHeader = DriveFileUtility.CreateClientFileHeader(existingHeader, odinContext),
                    OdinContext = odinContext,
                    DatabaseConnection = cn
                });
            }
        }

        public async Task UpdateActiveFileHeader(InternalDriveFileId targetFile, ServerFileHeader header, IOdinContext odinContext, DatabaseConnection cn,
            bool raiseEvent = false)
        {
            await UpdateActiveFileHeaderInternal(targetFile, header, false, odinContext, cn, raiseEvent);
        }

        public async Task UpdateTransferHistory(InternalDriveFileId file, OdinId recipient, UpdateTransferHistoryData updateData,
            IOdinContext odinContext,
            DatabaseConnection cn)
        {
            ServerFileHeader header = null;

            await PerformanceCounter.MeasureExecutionTime("UpdateTransferHistory",
                async () =>
                {
                    await AssertCanReadOrWriteToDrive(file.DriveId, odinContext, cn);

                    var mgr = await GetLongTermStorageManager(file.DriveId, cn);
                    var filePath = await mgr.GetServerFileHeaderPath(file.FileId);

                    async Task<ServerFileHeader> TryLockAndUpdate()
                    {
                        ServerFileHeader header = null;

                        _logger.LogDebug("UpdateTransferHistory trying to lock filePath:{filePath}", filePath);

                        await concurrentFileManager.WriteFileAsync(filePath, async _ =>
                        {
                            _logger.LogDebug("UpdateTransferHistory Successful Lock on:{filePath}", filePath);

                            var stopwatch = Stopwatch.StartNew();

                            //
                            // Get and validate the header
                            //
                            header = await mgr.GetServerFileHeader(file.FileId, byPassInternalFileLocking: true);
                            AssertValidFileSystemType(header.ServerMetadata);

                            if (stopwatch.ElapsedMilliseconds > 100)
                                _logger.LogDebug("UpdateTransferHistory Read header used {ms}", stopwatch.ElapsedMilliseconds);
                            stopwatch.Restart();

                            //
                            // update the transfer history record
                            //
                            var history = header.ServerMetadata.TransferHistory ?? new RecipientTransferHistory();
                            history.Recipients ??= new Dictionary<string, RecipientTransferHistoryItem>(StringComparer.InvariantCultureIgnoreCase);

                            if (!history.Recipients.TryGetValue(recipient, out var recipientItem))
                            {
                                recipientItem = new RecipientTransferHistoryItem();
                                history.Recipients.Add(recipient, recipientItem);
                            }

                            recipientItem.IsInOutbox = updateData.IsInOutbox.GetValueOrDefault(recipientItem.IsInOutbox);
                            recipientItem.IsReadByRecipient = updateData.IsReadByRecipient.GetValueOrDefault(recipientItem.IsReadByRecipient);
                            recipientItem.LastUpdated = UnixTimeUtc.Now();
                            recipientItem.LatestTransferStatus = updateData.LatestTransferStatus.GetValueOrDefault(recipientItem.LatestTransferStatus);
                            if (recipientItem.LatestTransferStatus == LatestTransferStatus.Delivered && updateData.VersionTag.HasValue)
                            {
                                recipientItem.LatestSuccessfullyDeliveredVersionTag = updateData.VersionTag.GetValueOrDefault();
                            }

                            header.ServerMetadata.TransferHistory = history;

                            _logger.LogDebug(
                                "Updating transfer history success on file:{file} for recipient:{recipient} Version:{versionTag}\t Status:{status}\t IsInOutbox:{outbox}\t IsReadByRecipient: {isRead}",
                                file,
                                recipient,
                                updateData.VersionTag,
                                updateData.LatestTransferStatus,
                                updateData.IsInOutbox,
                                updateData.IsReadByRecipient);

                            if (stopwatch.ElapsedMilliseconds > 100)
                                _logger.LogDebug("UpdateTransferHistory manage json used {ms}", stopwatch.ElapsedMilliseconds);
                            stopwatch.Restart();

                            //
                            // write to disk
                            //
                            await WriteFileHeaderInternal(header, cn, keepSameVersionTag: true, byPassInternalFileLocking: true);

                            if (stopwatch.ElapsedMilliseconds > 100)
                                _logger.LogDebug("UpdateTransferHistory write header file internal used {ms}", stopwatch.ElapsedMilliseconds);
                        });

                        return header;
                    }

                    var attempts = 7;
                    var delayMs = 200;

                    try
                    {
                        await TryRetry.WithBackoffAsync(
                            attempts: attempts,
                            exponentialBackoff: TimeSpan.FromMilliseconds(delayMs),
                            CancellationToken.None,
                            async () => { header = await TryLockAndUpdate(); });
                    }
                    catch (TryRetryException t)
                    {
                        _logger.LogError(t, "Failed to Lock and Update Transfer History after {attempts} " +
                                            "attempts with exponentialBackoff {delay}ms",
                            attempts,
                            delayMs);
                        throw;
                    }
                });

            if (await ShouldRaiseDriveEvent(file, cn))
            {
                await mediator.Publish(new DriveFileChangedNotification
                {
                    File = file,
                    ServerFileHeader = header,
                    OdinContext = odinContext,
                    DatabaseConnection = cn,
                    IgnoreFeedDistribution = true,
                    IgnoreReactionPreviewCalculation = true
                });
            }
        }

        public async Task UpdateBatch(InternalDriveFileId tempFile, InternalDriveFileId targetFile, BatchUpdateManifest manifest, IOdinContext odinContext,
            DatabaseConnection cn)
        {
            OdinValidationUtils.AssertNotEmptyGuid(manifest.NewVersionTag, nameof(manifest.NewVersionTag));

            //
            // Validations
            //
            var existingHeader = await this.GetServerFileHeaderInternal(targetFile, odinContext, cn);
            if (null == existingHeader)
            {
                throw new OdinClientException("File being updated does not exist", OdinClientErrorCode.InvalidFile);
            }

            DriveFileUtility.AssertVersionTagMatch(manifest.FileMetadata.VersionTag, existingHeader.FileMetadata.VersionTag);

            //
            // For the payloads, we have two sources and one set of operations
            // 1. manifest.FileMetadata.Payloads - indicates the payloads uploaded by the client for this batch update
            // 2. existingHeader.FileMetadata.Payloads - indicates the payload descriptors in current state on the server
            // 3. manifest.PayloadInstruction - this indicates what to do with the payloads on the file

            // now- each PayloadInstruction needs to be applied
            // to delete a payload, remove from disk and remove from the existingHeader.FileMetadata.Payloads
            // to add or append a payload, save on disk then upsert the value in existingHeader.FileMetadata.Payloads

            await cn.CreateCommitUnitOfWorkAsync(async () =>
            {
                var storageManager = await GetLongTermStorageManager(targetFile.DriveId, cn);
                var tempStorageManager = await GetTempStorageManager(targetFile.DriveId, cn);

                // 
                // Note: i've separated the payload instructions for readability
                // f
                foreach (var op in manifest.PayloadInstruction.Where(op => op.OperationType == PayloadUpdateOperationType.AppendOrOverwrite))
                {
                    // Here look at the incoming payloads because we're adding a new one or overwriting
                    var newDescriptor = manifest.FileMetadata.Payloads
                        .SingleOrDefault(pk => string.Equals(pk.Key, op.Key, StringComparison.InvariantCultureIgnoreCase));

                    if (newDescriptor == null)
                    {
                        var msg = $"Could not find payload with key {op.Key} in FileMetadata to perform operation {op.OperationType}";
                        throw new OdinClientException(msg, OdinClientErrorCode.InvalidPayload);
                    }

                    // Move the payload from the temp folder to the long term folder
                    var payloadExtension = DriveFileUtility.GetPayloadFileExtension(newDescriptor.Key, newDescriptor.Uid);
                    var sourceFile = await tempStorageManager.GetPath(tempFile.FileId, payloadExtension);
                    await storageManager.MovePayloadToLongTerm(targetFile.FileId, newDescriptor, sourceFile);

                    // Process thumbnails
                    var thumbs = newDescriptor.Thumbnails;
                    // clean up any old thumbnails (if we're overwriting one)
                    await storageManager.DeleteMissingThumbnailFiles(targetFile.FileId, thumbs);
                    foreach (var thumb in thumbs)
                    {
                        var extension = DriveFileUtility.GetThumbnailFileExtension(newDescriptor.Key, newDescriptor.Uid, thumb.PixelWidth,
                            thumb.PixelHeight);
                        var sourceThumbnail = await tempStorageManager.GetPath(tempFile.FileId, extension);
                        await storageManager.MoveThumbnailToLongTerm(targetFile.FileId, sourceThumbnail, newDescriptor, thumb);
                    }

                    //
                    // Upsert the descriptor in the existing header
                    //
                    var idx = existingHeader.FileMetadata.Payloads
                        .FindIndex(p => string.Equals(p.Key, op.Key, StringComparison.InvariantCultureIgnoreCase));

                    if (idx == -1)
                    {
                        // item was new
                        existingHeader.FileMetadata.Payloads.Add(newDescriptor);
                    }
                    else
                    {
                        existingHeader.FileMetadata.Payloads[idx] = newDescriptor;
                    }
                }

                // Delete operations are for existing payloads so we need to update the existing header
                foreach (var op in manifest.PayloadInstruction.Where(op => op.OperationType == PayloadUpdateOperationType.DeletePayload))
                {
                    var descriptor = existingHeader.FileMetadata.GetPayloadDescriptor(op.Key);
                    if (descriptor != null)
                    {
                        await this.DeletePayloadFromDiskInternal(targetFile, descriptor, cn);
                        existingHeader.FileMetadata.Payloads.RemoveAll(pk => string.Equals(pk.Key, op.Key, StringComparison.InvariantCultureIgnoreCase));
                    }
                }

                existingHeader.FileMetadata.VersionTag = manifest.NewVersionTag;
                await OverwriteMetadataInternal(manifest.KeyHeaderIv, existingHeader, manifest.FileMetadata,
                    manifest.ServerMetadata, odinContext, cn, manifest.NewVersionTag);

                if (await ShouldRaiseDriveEvent(targetFile, cn))
                {
                    await mediator.Publish(new DriveFileChangedNotification
                    {
                        File = targetFile,
                        ServerFileHeader = existingHeader,
                        OdinContext = odinContext,
                        DatabaseConnection = cn,
                        IgnoreFeedDistribution = false
                    });
                }
            });
        }

        private async Task<LongTermStorageManager> GetLongTermStorageManager(Guid driveId, DatabaseConnection cn)
        {
            var logger = loggerFactory.CreateLogger<LongTermStorageManager>();
            var drive = await DriveManager.GetDrive(driveId, cn, failIfInvalid: true);
            var manager = new LongTermStorageManager(drive, logger, driveFileReaderWriter);
            return manager;
        }

        private async Task<TempStorageManager> GetTempStorageManager(Guid driveId, DatabaseConnection cn)
        {
            var drive = await DriveManager.GetDrive(driveId, cn, failIfInvalid: true);
            var logger = loggerFactory.CreateLogger<TempStorageManager>();
            return new TempStorageManager(drive, driveFileReaderWriter, logger);
        }

        private async Task WriteFileHeaderInternal(ServerFileHeader header, DatabaseConnection cn, bool keepSameVersionTag = false,
            bool byPassInternalFileLocking = false)
        {
            if (!keepSameVersionTag)
            {
                header.FileMetadata.VersionTag = DriveFileUtility.CreateVersionTag();
            }

            header.FileMetadata.Updated = UnixTimeUtc.Now().milliseconds;

            var json = OdinSystemSerializer.Serialize(header);
            var jsonBytes = Encoding.UTF8.GetBytes(json);

            var payloadDiskUsage = header.FileMetadata.Payloads?.Sum(p => p.BytesWritten) ?? 0;
            var thumbnailDiskUsage = header.FileMetadata.Payloads?
                .SelectMany(p => p.Thumbnails ?? new List<ThumbnailDescriptor>())
                .Sum(pp => pp.BytesWritten) ?? 0;
            header.ServerMetadata.FileByteCount = payloadDiskUsage + thumbnailDiskUsage + jsonBytes.Length;

            //re-serlialize the json since we updated it
            json = OdinSystemSerializer.Serialize(header);
            jsonBytes = Encoding.UTF8.GetBytes(json);
            var stream = new MemoryStream(jsonBytes);

            var mgr = await GetLongTermStorageManager(header.FileMetadata.File.DriveId, cn);
            await mgr.WriteHeaderStream(header.FileMetadata.File.FileId, stream, byPassInternalFileLocking);
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

        private async Task<bool> ShouldRaiseDriveEvent(InternalDriveFileId file, DatabaseConnection cn)
        {
            return file.DriveId != (await DriveManager.GetDriveIdByAlias(SystemDriveConstants.TransientTempDrive, cn));
        }

        private async Task WriteDeletedFileHeader(ServerFileHeader existingHeader, IOdinContext odinContext, DatabaseConnection cn)
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

            var lts = await GetLongTermStorageManager(file.DriveId, cn);
            await lts.DeleteAttachments(file.FileId);
            await this.WriteFileHeaderInternal(deletedServerFileHeader, cn);

            if (await ShouldRaiseDriveEvent(file, cn))
            {
                await mediator.Publish(new DriveFileDeletedNotification
                {
                    PreviousServerFileHeader = existingHeader,
                    IsHardDelete = false,
                    File = file,
                    ServerFileHeader = deletedServerFileHeader,
                    SharedSecretEncryptedFileHeader = DriveFileUtility.CreateClientFileHeader(deletedServerFileHeader, odinContext),
                    OdinContext = odinContext,
                    DatabaseConnection = cn
                });
            }
        }

        private async Task<ServerFileHeader> CreateServerHeaderInternal(InternalDriveFileId targetFile, KeyHeader keyHeader, FileMetadata metadata,
            ServerMetadata serverMetadata, IOdinContext odinContext, DatabaseConnection cn)
        {
            serverMetadata.FileSystemType = GetFileSystemType();

            return new ServerFileHeader()
            {
                EncryptedKeyHeader =
                    metadata.IsEncrypted ? await this.EncryptKeyHeader(targetFile.DriveId, keyHeader, odinContext, cn) : EncryptedKeyHeader.Empty(),
                FileMetadata = metadata,
                ServerMetadata = serverMetadata
            };
        }

        private async Task<ServerFileHeader> GetServerFileHeaderInternal(InternalDriveFileId file, IOdinContext odinContext, DatabaseConnection cn)
        {
            var mgr = await GetLongTermStorageManager(file.DriveId, cn);
            var header = await mgr.GetServerFileHeader(file.FileId);

            if (null == header)
            {
                return null;
            }

            await driveAclAuthorizationService.AssertCallerHasPermission(header.ServerMetadata.AccessControlList, odinContext);

            return header;
        }

        private async Task OverwriteMetadataInternal(byte[] newKeyHeaderIv, ServerFileHeader existingServerHeader, FileMetadata newMetadata,
            ServerMetadata newServerMetadata,
            IOdinContext odinContext, DatabaseConnection cn, Guid? newVersionTag = null)
        {
            if (newMetadata.IsEncrypted && !ByteArrayUtil.IsStrongKey(newKeyHeaderIv))
            {
                throw new OdinClientException("KeyHeader Iv is not specified or is too weak");
            }

            if (null == existingServerHeader)
            {
                throw new OdinClientException("Cannot overwrite file that does not exist", OdinClientErrorCode.FileNotFound);
            }

            if (existingServerHeader.FileMetadata.FileState != FileState.Active)
            {
                throw new OdinClientException("Cannot update a non-active file", OdinClientErrorCode.CannotUpdateNonActiveFile);
            }

            if (existingServerHeader.FileMetadata.IsEncrypted != newMetadata.IsEncrypted)
            {
                throw new OdinClientException($"Cannot change encryption when storage intent is {StorageIntent.MetadataOnly} since your " +
                                              $"payloads might be invalidated", OdinClientErrorCode.ArgumentError);
            }

            if (newVersionTag == null)
            {
                DriveFileUtility.AssertVersionTagMatch(existingServerHeader.FileMetadata.VersionTag, newMetadata.VersionTag);
            }

            var targetFile = existingServerHeader.FileMetadata.File;
            newMetadata.File = targetFile;
            newMetadata.Created = existingServerHeader.FileMetadata.Created;
            newMetadata.GlobalTransitId = existingServerHeader.FileMetadata.GlobalTransitId;
            newMetadata.FileState = existingServerHeader.FileMetadata.FileState;
            newMetadata.Payloads = existingServerHeader.FileMetadata.Payloads;
            newMetadata.ReactionPreview = existingServerHeader.FileMetadata.ReactionPreview;

            newServerMetadata.FileSystemType = existingServerHeader.ServerMetadata.FileSystemType;

            //only change the IV if the file was encrypted
            if (existingServerHeader.FileMetadata.IsEncrypted)
            {
                // Critical Note: if this new key header's AES key does not match the
                // payload's encryption; the data is lost forever.  (for-ev-er, capish?)
                var storageKey = odinContext.PermissionsContext.GetDriveStorageKey(targetFile.DriveId);
                var existingDecryptedKeyHeader = existingServerHeader.EncryptedKeyHeader.DecryptAesToKeyHeader(ref storageKey);
                var newKeyHeader = new KeyHeader()
                {
                    Iv = newKeyHeaderIv,
                    AesKey = existingDecryptedKeyHeader.AesKey
                };

                existingServerHeader.EncryptedKeyHeader = await this.EncryptKeyHeader(targetFile.DriveId, newKeyHeader, odinContext, cn);
            }

            existingServerHeader.FileMetadata = newMetadata;
            existingServerHeader.ServerMetadata = newServerMetadata;
            if (newVersionTag == null)
            {
                await WriteFileHeaderInternal(existingServerHeader, cn);
            }
            else
            {
                existingServerHeader.FileMetadata.VersionTag = newVersionTag.Value;
                await WriteFileHeaderInternal(existingServerHeader, cn, keepSameVersionTag: true);
            }
        }

        private async Task DeletePayloadFromDiskInternal(InternalDriveFileId file, PayloadDescriptor descriptor, DatabaseConnection cn)
        {
            var lts = await GetLongTermStorageManager(file.DriveId, cn);

            // Delete the thumbnail files for this payload
            foreach (var thumb in descriptor.Thumbnails ?? new List<ThumbnailDescriptor>())
            {
                await lts.DeleteThumbnailFile(file.FileId, descriptor.Key, descriptor.Uid, thumb.PixelWidth, thumb.PixelHeight);
            }

            // Delete the payload file
            await lts.DeletePayloadFile(file.FileId, descriptor);
        }
    }
}