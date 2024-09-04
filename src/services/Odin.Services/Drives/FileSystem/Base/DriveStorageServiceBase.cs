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
using Odin.Core.Storage.SQLite.IdentityDatabase;
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


namespace Odin.Services.Drives.FileSystem.Base
{
    public abstract class DriveStorageServiceBase(
        ILoggerFactory loggerFactory,
        IMediator mediator,
        IDriveAclAuthorizationService driveAclAuthorizationService,
        DriveManager driveManager,
        ConcurrentFileManager concurrentFileManager,
        DriveFileReaderWriter driveFileReaderWriter,
        DriveDatabaseHost driveDatabaseHost) : RequirePermissionsBase
    {
        private readonly ILogger<DriveStorageServiceBase> _logger = loggerFactory.CreateLogger<DriveStorageServiceBase>();

        protected override DriveManager DriveManager { get; } = driveManager;

        /// <summary>
        /// Gets the <see cref="FileSystemType"/> of which the inheriting class manages
        /// </summary>
        public abstract FileSystemType GetFileSystemType();

        public async Task<SharedSecretEncryptedFileHeader> GetSharedSecretEncryptedHeader(InternalDriveFileId file, IOdinContext odinContext,
            IdentityDatabase db)
        {
            var serverFileHeader = await this.GetServerFileHeader(file, odinContext, db);
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
            GetPayloadSharedSecretEncryptedKeyHeader(InternalDriveFileId file, string payloadKey, IOdinContext odinContext, IdentityDatabase db)
        {
            var serverFileHeader = await this.GetServerFileHeader(file, odinContext, db);
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

        public async Task<InternalDriveFileId> CreateInternalFileId(Guid driveId, IdentityDatabase db)
        {
            var lts = await GetLongTermStorageManager(driveId, db);
            var df = new InternalDriveFileId()
            {
                FileId = lts.CreateFileId(),
                DriveId = driveId,
            };

            return df;
        }

        private async Task UpdateActiveFileHeaderInternal(InternalDriveFileId targetFile, ServerFileHeader header, bool keepSameVersionTag,
            IOdinContext odinContext, IdentityDatabase db,
            bool raiseEvent = false, bool ignoreFeedDistribution = false)
        {
            if (!header.IsValid())
            {
                throw new OdinSystemException("An invalid header was passed to the update header method.  You need more checks in place before getting here");
            }

            await AssertCanWriteToDrive(targetFile.DriveId, odinContext, db);

            //short circuit
            var fileExists = await FileExists(targetFile, odinContext, db);
            if (!fileExists)
            {
                await WriteNewFileHeader(targetFile, header, odinContext, db, raiseEvent);
                return;
            }

            var metadata = header.FileMetadata;

            //TODO: need to encrypt the metadata parts
            metadata.File = targetFile; //TBH it's strange having this but we need the metadata to have the file and drive embedded

            if (metadata.FileState != FileState.Active)
            {
                throw new OdinClientException("Cannot update non-active file", OdinClientErrorCode.CannotUpdateNonActiveFile);
            }

            var existingHeader = await this.GetServerFileHeaderInternal(targetFile, odinContext, db);
            metadata.Created = existingHeader.FileMetadata.Created;
            metadata.GlobalTransitId = existingHeader.FileMetadata.GlobalTransitId;
            metadata.FileState = existingHeader.FileMetadata.FileState;
            metadata.SenderOdinId = existingHeader.FileMetadata.SenderOdinId;

            await WriteFileHeaderInternal(header, db, keepSameVersionTag);

            //clean up temp storage
            var tsm = await GetTempStorageManager(targetFile.DriveId, db);
            await tsm.EnsureDeleted(targetFile.FileId);

            //HACKed in for Feed drive
            if (raiseEvent)
            {
                if (await ShouldRaiseDriveEvent(targetFile, db))
                {
                    await mediator.Publish(new DriveFileChangedNotification
                    {
                        File = targetFile,
                        ServerFileHeader = header,
                        OdinContext = odinContext,
                        db = db,
                        IgnoreFeedDistribution = ignoreFeedDistribution
                    });
                }
            }
        }

        /// <summary>
        /// Writes a new file header w/o checking for an existing one
        /// </summary>
        public async Task WriteNewFileHeader(InternalDriveFileId targetFile, ServerFileHeader header, IOdinContext odinContext, IdentityDatabase db,
            bool raiseEvent = false)
        {
            if (!header.IsValid())
            {
                throw new OdinSystemException("An invalid header was passed to the update header method.  You need more checks in place before getting here");
            }

            await AssertCanWriteToDrive(targetFile.DriveId, odinContext, db);

            var metadata = header.FileMetadata;

            //TODO: need to encrypt the metadata parts
            metadata.File = targetFile; //TBH it's strange having this but we need the metadata to have the file and drive embedded
            metadata.Created = header.FileMetadata.Created != 0 ? header.FileMetadata.Created : UnixTimeUtc.Now().milliseconds;
            metadata.FileState = FileState.Active;

            await WriteFileHeaderInternal(header, db);

            //clean up temp storage
            var tsm = await GetTempStorageManager(targetFile.DriveId, db);
            await tsm.EnsureDeleted(targetFile.FileId);

            //HACKed in for Feed drive
            if (raiseEvent)
            {
                if (await ShouldRaiseDriveEvent(targetFile, db))
                {
                    await mediator.Publish(new DriveFileAddedNotification
                    {
                        File = targetFile,
                        ServerFileHeader = header,
                        OdinContext = odinContext,
                        db = db
                    });
                }
            }
        }

        public async Task<uint> WriteTempStream(InternalDriveFileId file, string extension, Stream stream, IOdinContext odinContext, IdentityDatabase db)
        {
            await AssertCanWriteToDrive(file.DriveId, odinContext, db);
            var tsm = await GetTempStorageManager(file.DriveId, db);
            return await tsm.WriteStream(file.FileId, extension, stream);
        }

        /// <summary>
        /// Reads the whole file so be sure this is only used on small'ish files; ones you're ok with loaded fully into server-memory
        /// </summary>
        /// <returns></returns>
        public async Task<byte[]> GetAllFileBytesFromTempFile(InternalDriveFileId file, string extension, IOdinContext odinContext, IdentityDatabase db)
        {
            await this.AssertCanReadDrive(file.DriveId, odinContext, db);
            var tsm = await GetTempStorageManager(file.DriveId, db);
            var bytes = await tsm.GetAllFileBytes(file.FileId, extension);
            return bytes;
        }

        public async Task<byte[]> GetAllFileBytesFromTempFileForWriting(InternalDriveFileId file, string extension, IOdinContext odinContext,
            IdentityDatabase db)
        {
            await AssertCanWriteToDrive(file.DriveId, odinContext, db);
            var tsm = await GetTempStorageManager(file.DriveId, db);
            return await tsm.GetAllFileBytes(file.FileId, extension);
        }

        public async Task DeleteTempFile(InternalDriveFileId file, string extension, IOdinContext odinContext, IdentityDatabase db)
        {
            await AssertCanWriteToDrive(file.DriveId, odinContext, db);
            var tsm = await GetTempStorageManager(file.DriveId, db);
            await tsm.EnsureDeleted(file.FileId, extension);
        }

        public async Task DeleteTempFiles(InternalDriveFileId file, IOdinContext odinContext, IdentityDatabase db)
        {
            await AssertCanWriteToDrive(file.DriveId, odinContext, db);

            var tsm = await GetTempStorageManager(file.DriveId, db);
            await tsm.EnsureDeleted(file.FileId);
        }

        public async Task<(Stream stream, ThumbnailDescriptor thumbnail)> GetThumbnailPayloadStream(InternalDriveFileId file, int width, int height,
            string payloadKey, UnixTimeUtcUnique payloadUid, IOdinContext odinContext, IdentityDatabase db, bool directMatchOnly = false)
        {
            await AssertCanReadDrive(file.DriveId, odinContext, db);

            DriveFileUtility.AssertValidPayloadKey(payloadKey);
            var lts = await GetLongTermStorageManager(file.DriveId, db);

            //Note: calling to get the file header so we can ensure the caller can read this file
            var header = await this.GetServerFileHeader(file, odinContext, db);
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
                    var drive = await DriveManager.GetDrive(file.DriveId, db);
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
                var drive = await DriveManager.GetDrive(file.DriveId, db);
                if (drive.TargetDriveInfo == SystemDriveConstants.FeedDrive)
                {
                    return (Stream.Null, nextSizeUp);
                }

                throw;
            }
        }

        public async Task<Guid> DeletePayload(InternalDriveFileId file, string key, Guid versionTag, IOdinContext odinContext, IdentityDatabase db)
        {
            await AssertCanWriteToDrive(file.DriveId, odinContext, db);

            //Note: calling to get the file header so we can ensure the caller can read this file
            var header = await this.GetServerFileHeader(file, odinContext, db);
            DriveFileUtility.AssertVersionTagMatch(header.FileMetadata.VersionTag, versionTag);

            var descriptorIndex = header.FileMetadata.Payloads?.FindIndex(p => string.Equals(p.Key, key, StringComparison.InvariantCultureIgnoreCase)) ?? -1;

            if (descriptorIndex == -1)
            {
                return Guid.Empty;
            }

            var lts = await GetLongTermStorageManager(file.DriveId, db);
            var descriptor = header.FileMetadata.Payloads![descriptorIndex];

            // Delete the thumbnail files for this payload
            foreach (var thumb in descriptor.Thumbnails ?? new List<ThumbnailDescriptor>())
            {
                await lts.DeleteThumbnailFile(file.FileId, key, descriptor.Uid, thumb.PixelWidth, thumb.PixelHeight);
            }

            // Delete the payload file
            await lts.DeletePayloadFile(file.FileId, descriptor);

            header.FileMetadata.Payloads!.RemoveAt(descriptorIndex);
            await UpdateActiveFileHeader(file, header, odinContext, db);
            return header.FileMetadata.VersionTag.GetValueOrDefault(); // this works because because pass header all the way
        }

        public async Task<ServerFileHeader> CreateServerFileHeader(InternalDriveFileId file, KeyHeader keyHeader, FileMetadata metadata,
            ServerMetadata serverMetadata, IOdinContext odinContext, IdentityDatabase db)
        {
            return await CreateServerHeaderInternal(file, keyHeader, metadata, serverMetadata, odinContext, db);
        }

        private async Task<EncryptedKeyHeader> EncryptKeyHeader(Guid driveId, KeyHeader keyHeader, IOdinContext odinContext, IdentityDatabase db)
        {
            var storageKey = odinContext.PermissionsContext.GetDriveStorageKey(driveId);

            (await this.DriveManager.GetDrive(driveId, db)).AssertValidStorageKey(storageKey);

            var encryptedKeyHeader = EncryptedKeyHeader.EncryptKeyHeaderAes(keyHeader, keyHeader.Iv, ref storageKey);
            return encryptedKeyHeader;
        }

        public async Task<bool> CallerHasPermissionToFile(InternalDriveFileId file, IOdinContext odinContext, IdentityDatabase db)
        {
            var lts = await GetLongTermStorageManager(file.DriveId, db);
            var header = await lts.GetServerFileHeader(file.FileId, db);

            if (null == header)
            {
                return false;
            }

            return await driveAclAuthorizationService.CallerHasPermission(header.ServerMetadata.AccessControlList, odinContext);
        }

        public async Task<ServerFileHeader> GetServerFileHeader(InternalDriveFileId file, IOdinContext odinContext, IdentityDatabase db)
        {
            await AssertCanReadDrive(file.DriveId, odinContext, db);
            var header = await GetServerFileHeaderInternal(file, odinContext, db);

            if (header == null)
            {
                return null;
            }

            AssertValidFileSystemType(header.ServerMetadata);
            return header;
        }

        public async Task<ServerFileHeader> GetServerFileHeaderForWriting(InternalDriveFileId file, IOdinContext odinContext, IdentityDatabase db)
        {
            await AssertCanWriteToDrive(file.DriveId, odinContext, db);
            var header = await GetServerFileHeaderInternal(file, odinContext, db);

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
        public async Task<FileSystemType> ResolveFileSystemType(InternalDriveFileId file, IOdinContext odinContext, IdentityDatabase db)
        {
            await AssertCanReadOrWriteToDrive(file.DriveId, odinContext, db);

            var header = await GetServerFileHeaderInternal(file, odinContext, db);
            return header.ServerMetadata.FileSystemType;
        }

        public async Task<PayloadStream> GetPayloadStream(InternalDriveFileId file, string key, FileChunk chunk, IOdinContext odinContext,
            IdentityDatabase db)
        {
            await AssertCanReadDrive(file.DriveId, odinContext, db);
            DriveFileUtility.AssertValidPayloadKey(key);

            //Note: calling to get the file header will also
            //ensure the caller can touch this file.
            var header = await GetServerFileHeader(file, odinContext, db);
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
                var lts = await GetLongTermStorageManager(file.DriveId, db);
                var stream = await lts.GetPayloadStream(file.FileId, descriptor, chunk);
                return new PayloadStream(descriptor, stream);
            }
            catch (OdinFileHeaderHasCorruptPayloadException)
            {
                var drive = await DriveManager.GetDrive(file.DriveId, db);
                if (drive.TargetDriveInfo == SystemDriveConstants.FeedDrive)
                {
                    return null;
                }

                throw;
            }
        }

        public async Task<bool> FileExists(InternalDriveFileId file, IOdinContext odinContext, IdentityDatabase db)
        {
            await AssertCanReadOrWriteToDrive(file.DriveId, odinContext, db);
            var lts = await GetLongTermStorageManager(file.DriveId, db);
            return await lts.HeaderFileExists(file.FileId, db);
        }

        public async Task SoftDeleteLongTermFile(InternalDriveFileId file, IOdinContext odinContext, IdentityDatabase db)
        {
            await AssertCanWriteToDrive(file.DriveId, odinContext, db);

            var existingHeader = await this.GetServerFileHeaderInternal(file, odinContext, db);

            await WriteDeletedFileHeader(existingHeader, odinContext, db);
        }

        public async Task HardDeleteLongTermFile(InternalDriveFileId file, IOdinContext odinContext, IdentityDatabase db)
        {
            await AssertCanWriteToDrive(file.DriveId, odinContext, db);

            var lts = await GetLongTermStorageManager(file.DriveId, db);
            await lts.HardDelete(file.FileId, db);

            if (await ShouldRaiseDriveEvent(file, db))
            {
                await mediator.Publish(new DriveFileDeletedNotification
                {
                    IsHardDelete = true,
                    File = file,
                    ServerFileHeader = null,
                    SharedSecretEncryptedFileHeader = null,
                    OdinContext = odinContext,
                    db = db
                });
            }
        }

        public async Task CommitNewFile(InternalDriveFileId targetFile, KeyHeader keyHeader, FileMetadata metadata, ServerMetadata serverMetadata,
            bool? ignorePayload, IOdinContext odinContext, IdentityDatabase db)
        {
            await AssertCanWriteToDrive(targetFile.DriveId, odinContext, db);

            metadata.File = targetFile;
            serverMetadata.FileSystemType = GetFileSystemType();

            var storageManager = await GetLongTermStorageManager(targetFile.DriveId, db);
            var tempStorageManager = await GetTempStorageManager(targetFile.DriveId, db);

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
            var serverHeader = await CreateServerHeaderInternal(targetFile, keyHeader, metadata, serverMetadata, odinContext, db);

            await WriteNewFileHeader(targetFile, serverHeader, odinContext, db);

            //clean up temp storage
            await tempStorageManager.EnsureDeleted(targetFile.FileId);

            if (await ShouldRaiseDriveEvent(targetFile, db))
            {
                await mediator.Publish(new DriveFileAddedNotification
                {
                    File = targetFile,
                    ServerFileHeader = serverHeader,
                    OdinContext = odinContext,
                    db = db
                });
            }
        }

        public async Task OverwriteFile(InternalDriveFileId tempFile, InternalDriveFileId targetFile, KeyHeader keyHeader, FileMetadata newMetadata,
            ServerMetadata serverMetadata, bool? ignorePayload, IOdinContext odinContext, IdentityDatabase db)
        {
            await AssertCanWriteToDrive(targetFile.DriveId, odinContext, db);

            var existingServerHeader = await this.GetServerFileHeader(targetFile, odinContext, db);
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

            var longTermStorageManager = await GetLongTermStorageManager(targetFile.DriveId, db);
            var tempStorageManager = await GetTempStorageManager(tempFile.DriveId, db);

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
                EncryptedKeyHeader = await this.EncryptKeyHeader(tempFile.DriveId, keyHeader, odinContext, db),
                FileMetadata = newMetadata,
                ServerMetadata = serverMetadata
            };

            await WriteFileHeaderInternal(serverHeader, db);

            //clean up temp storage
            await tempStorageManager.EnsureDeleted(targetFile.FileId);

            if (await ShouldRaiseDriveEvent(targetFile, db))
            {
                await mediator.Publish(new DriveFileChangedNotification
                {
                    File = targetFile,
                    ServerFileHeader = serverHeader,
                    OdinContext = odinContext,
                    db = db
                });
            }
        }

        public async Task<Guid> UpdatePayloads(
            InternalDriveFileId tempSourceFile,
            InternalDriveFileId targetFile,
            List<PayloadDescriptor> incomingPayloads,
            IOdinContext odinContext,
            IdentityDatabase db)
        {
            await AssertCanWriteToDrive(targetFile.DriveId, odinContext, db);

            var existingServerHeader = await this.GetServerFileHeader(targetFile, odinContext, db);
            if (null == existingServerHeader)
            {
                throw new OdinClientException("Invalid target file", OdinClientErrorCode.FileNotFound);
            }

            if (existingServerHeader.FileMetadata.FileState != FileState.Active)
            {
                throw new OdinClientException("Cannot update a non-active file", OdinClientErrorCode.CannotUpdateNonActiveFile);
            }

            var storageManager = await GetLongTermStorageManager(targetFile.DriveId, db);
            var tempStorageManager = await GetTempStorageManager(tempSourceFile.DriveId, db);

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

            await WriteFileHeaderInternal(existingServerHeader, db);

            //clean up temp storage
            await tempStorageManager.EnsureDeleted(targetFile.FileId);

            if (await ShouldRaiseDriveEvent(targetFile, db))
            {
                await mediator.Publish(new DriveFileChangedNotification
                {
                    File = targetFile,
                    ServerFileHeader = existingServerHeader,
                    OdinContext = odinContext,
                    db = db
                });
            }

            return existingServerHeader.FileMetadata.VersionTag.GetValueOrDefault();
        }

        public async Task OverwriteMetadata(byte[] newKeyHeaderIv, InternalDriveFileId targetFile, FileMetadata newMetadata, ServerMetadata newServerMetadata,
            IOdinContext odinContext, IdentityDatabase db)
        {
            await AssertCanWriteToDrive(targetFile.DriveId, odinContext, db);

            if (newMetadata.IsEncrypted && !ByteArrayUtil.IsStrongKey(newKeyHeaderIv))
            {
                throw new OdinClientException("KeyHeader Iv is not specified or is too weak");
            }

            var existingServerHeader = await this.GetServerFileHeader(targetFile, odinContext, db);

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

            DriveFileUtility.AssertVersionTagMatch(existingServerHeader.FileMetadata.VersionTag, newMetadata.VersionTag);

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

                existingServerHeader.EncryptedKeyHeader = await this.EncryptKeyHeader(targetFile.DriveId, newKeyHeader, odinContext, db);
            }

            existingServerHeader.FileMetadata = newMetadata;
            existingServerHeader.ServerMetadata = newServerMetadata;

            await WriteFileHeaderInternal(existingServerHeader, db);

            //clean up temp storage
            var tsm = await GetTempStorageManager(targetFile.DriveId, db);
            await tsm.EnsureDeleted(targetFile.FileId);

            if (await ShouldRaiseDriveEvent(targetFile, db))
            {
                await mediator.Publish(new DriveFileChangedNotification
                {
                    File = targetFile,
                    ServerFileHeader = existingServerHeader,
                    OdinContext = odinContext,
                    db = db
                });
            }
        }

        public async Task UpdateReactionSummary(InternalDriveFileId targetFile, ReactionSummary summary, IOdinContext odinContext, IdentityDatabase db)
        {
            odinContext.PermissionsContext.AssertHasAtLeastOneDrivePermission(
                targetFile.DriveId, DrivePermission.React, DrivePermission.Comment, DrivePermission.Write);
            var lts = await GetLongTermStorageManager(targetFile.DriveId, db);
            var existingHeader = await lts.GetServerFileHeader(targetFile.FileId, db);
            existingHeader.FileMetadata.ReactionPreview = summary;

            await lts.SaveReactionHistory(targetFile.FileId, summary, db);

            //clean up temp storage
            var tsm = await GetTempStorageManager(targetFile.DriveId, db);
            await tsm.EnsureDeleted(targetFile.FileId);

            if (await ShouldRaiseDriveEvent(targetFile, db))
            {
                await mediator.Publish(new ReactionPreviewUpdatedNotification
                {
                    File = targetFile,
                    ServerFileHeader = existingHeader,
                    SharedSecretEncryptedFileHeader = DriveFileUtility.CreateClientFileHeader(existingHeader, odinContext),
                    OdinContext = odinContext,
                    db = db
                });
            }
        }


        public async Task UpdateTransferHistory(InternalDriveFileId file, OdinId recipient, UpdateTransferHistoryData updateData,
            IOdinContext odinContext,
            IdentityDatabase db)
        {
            ServerFileHeader header = null;

            await PerformanceCounter.MeasureExecutionTime("UpdateTransferHistory",
                async () =>
                {
                    await AssertCanReadOrWriteToDrive(file.DriveId, odinContext, db);

                    var mgr = await GetLongTermStorageManager(file.DriveId, db);
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
                            header = await mgr.GetServerFileHeader(file.FileId, db);
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

                            await mgr.SaveTransferHistory(file.FileId, history, db);

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

            if (await ShouldRaiseDriveEvent(file, db))
            {
                await mediator.Publish(new DriveFileChangedNotification
                {
                    File = file,
                    ServerFileHeader = header,
                    OdinContext = odinContext,
                    db = db,
                    IgnoreFeedDistribution = true,
                    IgnoreReactionPreviewCalculation = true
                });
            }
        }

        // Feed drive hacks

        public async Task WriteNewFileToFeedDrive(KeyHeader keyHeader, FileMetadata fileMetadata, IOdinContext odinContext, IdentityDatabase db)
        {
            // Method assumes you ensured the file was unique by some other method

            var feedDriveId = await DriveManager.GetDriveIdByAlias(SystemDriveConstants.FeedDrive, db);
            await AssertCanWriteToDrive(feedDriveId.GetValueOrDefault(), odinContext, db);
            var file = await this.CreateInternalFileId(feedDriveId.GetValueOrDefault(), db);

            var serverMetadata = new ServerMetadata()
            {
                AccessControlList = AccessControlList.OwnerOnly,
                AllowDistribution = false
            };

            //we don't accept uniqueIds into the feed
            fileMetadata.AppData.UniqueId = null;

            var serverFileHeader = await this.CreateServerFileHeader(file, keyHeader, fileMetadata, serverMetadata, odinContext, db);
            await this.WriteNewFileHeader(file, serverFileHeader, odinContext, db, raiseEvent: true);
        }

        public async Task ReplaceFileMetadataOnFeedDrive(InternalDriveFileId file, FileMetadata fileMetadata, IOdinContext odinContext, IdentityDatabase db,
            bool bypassCallerCheck = false)
        {
            await AssertCanWriteToDrive(file.DriveId, odinContext, db);
            var header = await GetServerFileHeaderInternal(file, odinContext, db);
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

            var feedDriveId = await DriveManager.GetDriveIdByAlias(SystemDriveConstants.FeedDrive, db);
            if (file.DriveId != feedDriveId)
            {
                throw new OdinSystemException("Method cannot be used on drive");
            }

            if (!bypassCallerCheck) //eww
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

            await this.UpdateActiveFileHeader(file, header, odinContext, db, raiseEvent: true);
            if (header.FileMetadata.ReactionPreview == null)
            {
                var lts = await GetLongTermStorageManager(file.DriveId, db);
                await lts.DeleteReactionSummary(file.FileId, db);
            }
            else
            {
                await UpdateReactionSummary(file, header.FileMetadata.ReactionPreview, odinContext, db);
            }
            
        }

        public async Task RemoveFeedDriveFile(InternalDriveFileId file, IOdinContext odinContext, IdentityDatabase db)
        {
            await AssertCanWriteToDrive(file.DriveId, odinContext, db);
            var header = await GetServerFileHeaderInternal(file, odinContext, db);
            AssertValidFileSystemType(header.ServerMetadata);
            var feedDriveId = await DriveManager.GetDriveIdByAlias(SystemDriveConstants.FeedDrive, db);

            if (file.DriveId != feedDriveId)
            {
                throw new OdinSystemException("Method cannot be used on drive");
            }

            //S0510
            if (header.FileMetadata.SenderOdinId != odinContext.GetCallerOdinIdOrFail())
            {
                throw new OdinSecurityException("Invalid caller");
            }

            await WriteDeletedFileHeader(header, odinContext, db);
        }

        public async Task UpdateReactionPreviewOnFeedDrive(InternalDriveFileId targetFile, ReactionSummary summary, IOdinContext odinContext,
            IdentityDatabase db)
        {
            await AssertCanWriteToDrive(targetFile.DriveId, odinContext, db);
            var feedDriveId = await DriveManager.GetDriveIdByAlias(SystemDriveConstants.FeedDrive, db);
            if (targetFile.DriveId != feedDriveId)
            {
                throw new OdinSystemException("Cannot update reaction preview on this drive");
            }

            var lts = await GetLongTermStorageManager(targetFile.DriveId, db);
            var existingHeader = await lts.GetServerFileHeader(targetFile.FileId, db);

            //S0510
            if (existingHeader.FileMetadata.SenderOdinId != odinContext.Caller.OdinId)
            {
                throw new OdinSecurityException("Invalid caller");
            }

            existingHeader.FileMetadata.ReactionPreview = summary;
            await WriteFileHeaderInternal(existingHeader, db);

            //clean up temp storage
            var tsm = await GetTempStorageManager(targetFile.DriveId, db);
            await tsm.EnsureDeleted(targetFile.FileId);

            if (await ShouldRaiseDriveEvent(targetFile, db))
            {
                await mediator.Publish(new ReactionPreviewUpdatedNotification
                {
                    File = targetFile,
                    ServerFileHeader = existingHeader,
                    SharedSecretEncryptedFileHeader = DriveFileUtility.CreateClientFileHeader(existingHeader, odinContext),
                    OdinContext = odinContext,
                    db = db
                });
            }
        }

        public async Task UpdateActiveFileHeader(InternalDriveFileId targetFile, ServerFileHeader header, IOdinContext odinContext, IdentityDatabase db,
            bool raiseEvent = false)
        {
            await UpdateActiveFileHeaderInternal(targetFile, header, false, odinContext, db, raiseEvent);
        }


        private async Task<LongTermStorageManager> GetLongTermStorageManager(Guid driveId, IdentityDatabase db)
        {
            var logger = loggerFactory.CreateLogger<LongTermStorageManager>();
            var drive = await DriveManager.GetDrive(driveId, db, failIfInvalid: true);
            var manager = new LongTermStorageManager(drive, logger, driveFileReaderWriter, driveDatabaseHost, GetFileSystemType());
            return manager;
        }

        private async Task<TempStorageManager> GetTempStorageManager(Guid driveId, IdentityDatabase db)
        {
            var drive = await DriveManager.GetDrive(driveId, db, failIfInvalid: true);
            var logger = loggerFactory.CreateLogger<TempStorageManager>();
            return new TempStorageManager(drive, driveFileReaderWriter, logger);
        }

        private async Task WriteFileHeaderInternal(ServerFileHeader header, IdentityDatabase db, bool keepSameVersionTag = false)
        {
            if (!keepSameVersionTag)
            {
                header.FileMetadata.VersionTag = SequentialGuid.CreateGuid();
            }

            header.FileMetadata.Updated = UnixTimeUtc.Now().milliseconds;

            var json = OdinSystemSerializer.Serialize(header);
            var jsonBytes = Encoding.UTF8.GetBytes(json);

            var payloadDiskUsage = header.FileMetadata.Payloads?.Sum(p => p.BytesWritten) ?? 0;
            var thumbnailDiskUsage = header.FileMetadata.Payloads?
                .SelectMany(p => p.Thumbnails ?? new List<ThumbnailDescriptor>())
                .Sum(pp => pp.BytesWritten) ?? 0;
            header.ServerMetadata.FileByteCount = payloadDiskUsage + thumbnailDiskUsage + jsonBytes.Length;

            var mgr = await GetLongTermStorageManager(header.FileMetadata.File.DriveId, db);
            await mgr.SaveFileHeader(header, db);
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

        private async Task<bool> ShouldRaiseDriveEvent(InternalDriveFileId file, IdentityDatabase db)
        {
            return file.DriveId != (await DriveManager.GetDriveIdByAlias(SystemDriveConstants.TransientTempDrive, db));
        }

        private async Task WriteDeletedFileHeader(ServerFileHeader existingHeader, IOdinContext odinContext, IdentityDatabase db)
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

            // TODO CONNECTIONS - need a transaction here
            var lts = await GetLongTermStorageManager(file.DriveId, db);
            await lts.DeleteAttachments(file.FileId);
            await this.WriteFileHeaderInternal(deletedServerFileHeader, db);
            await lts.DeleteReactionSummary(deletedServerFileHeader.FileMetadata.File.FileId, db);
            await lts.DeleteTransferHistory(deletedServerFileHeader.FileMetadata.File.FileId, db);

            if (await ShouldRaiseDriveEvent(file, db))
            {
                await mediator.Publish(new DriveFileDeletedNotification
                {
                    PreviousServerFileHeader = existingHeader,
                    IsHardDelete = false,
                    File = file,
                    ServerFileHeader = deletedServerFileHeader,
                    SharedSecretEncryptedFileHeader = DriveFileUtility.CreateClientFileHeader(deletedServerFileHeader, odinContext),
                    OdinContext = odinContext,
                    db = db
                });
            }
        }

        private async Task<ServerFileHeader> CreateServerHeaderInternal(InternalDriveFileId targetFile, KeyHeader keyHeader, FileMetadata metadata,
            ServerMetadata serverMetadata, IOdinContext odinContext, IdentityDatabase db)
        {
            serverMetadata.FileSystemType = GetFileSystemType();

            return new ServerFileHeader()
            {
                EncryptedKeyHeader =
                    metadata.IsEncrypted ? await this.EncryptKeyHeader(targetFile.DriveId, keyHeader, odinContext, db) : EncryptedKeyHeader.Empty(),
                FileMetadata = metadata,
                ServerMetadata = serverMetadata
            };
        }

        private async Task<ServerFileHeader> GetServerFileHeaderInternal(InternalDriveFileId file, IOdinContext odinContext, IdentityDatabase db)
        {
            var mgr = await GetLongTermStorageManager(file.DriveId, db);
            var header = await mgr.GetServerFileHeader(file.FileId, db);

            if (null == header)
            {
                return null;
            }

            await driveAclAuthorizationService.AssertCallerHasPermission(header.ServerMetadata.AccessControlList, odinContext);

            return header;
        }
    }
}