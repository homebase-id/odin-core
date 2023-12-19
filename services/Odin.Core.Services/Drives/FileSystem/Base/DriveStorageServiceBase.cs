using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Dawn;
using MediatR;
using Microsoft.Extensions.Logging;
using Odin.Core.Exceptions;
using Odin.Core.Serialization;
using Odin.Core.Services.Apps;
using Odin.Core.Services.Authorization.Acl;
using Odin.Core.Services.Base;
using Odin.Core.Services.Drives.DriveCore.Storage;
using Odin.Core.Services.Drives.Management;
using Odin.Core.Services.Mediator;
using Odin.Core.Services.Peer.Encryption;
using Odin.Core.Storage;
using Odin.Core.Time;


namespace Odin.Core.Services.Drives.FileSystem.Base
{
    public abstract class DriveStorageServiceBase : RequirePermissionsBase
    {
        private readonly IDriveAclAuthorizationService _driveAclAuthorizationService;
        private readonly IMediator _mediator;
        private readonly ILoggerFactory _loggerFactory;
        private readonly DriveManager _driveManager;

        protected DriveStorageServiceBase(
            OdinContextAccessor contextAccessor,
            ILoggerFactory loggerFactory,
            IMediator mediator,
            IDriveAclAuthorizationService driveAclAuthorizationService,
            DriveManager driveManager)
        {
            ContextAccessor = contextAccessor;
            _loggerFactory = loggerFactory;
            _mediator = mediator;
            _driveAclAuthorizationService = driveAclAuthorizationService;
            _driveManager = driveManager;
            DriveManager = driveManager;
        }

        protected override DriveManager DriveManager { get; }
        protected override OdinContextAccessor ContextAccessor { get; }

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

            var result = DriveFileUtility.ConvertToSharedSecretEncryptedClientFileHeader(serverFileHeader, ContextAccessor);
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

        public InternalDriveFileId CreateInternalFileId(Guid driveId)
        {
            //TODO: need a permission specifically for writing to the temp drive
            //AssertCanWriteToDrive(driveId);

            var df = new InternalDriveFileId()
            {
                FileId = GetLongTermStorageManager(driveId).CreateFileId(),
                DriveId = driveId,
            };

            return df;
        }

        public async Task UpdateActiveFileHeader(InternalDriveFileId targetFile, ServerFileHeader header, bool raiseEvent = false)
        {
            Guard.Argument(header, nameof(header)).NotNull();
            Guard.Argument(header, nameof(header)).Require(x => x.IsValid());

            AssertCanWriteToDrive(targetFile.DriveId);

            var metadata = header.FileMetadata;

            //TODO: need to encrypt the metadata parts
            metadata.File = targetFile; //TBH it's strange having this but we need the metadata to have the file and drive embedded

            bool wasAnUpdate = false;
            if (this.FileExists(targetFile))
            {
                if (metadata.FileState != FileState.Active)
                {
                    throw new OdinClientException("Cannot update non-active file", OdinClientErrorCode.CannotUpdateNonActiveFile);
                }

                var existingHeader = await this.GetServerFileHeaderInternal(targetFile);
                metadata.Created = existingHeader.FileMetadata.Created;
                metadata.GlobalTransitId = existingHeader.FileMetadata.GlobalTransitId;
                metadata.FileState = existingHeader.FileMetadata.FileState;
                metadata.SenderOdinId = existingHeader.FileMetadata.SenderOdinId;
                wasAnUpdate = true;
            }
            else
            {
                metadata.Created = UnixTimeUtc.Now().milliseconds;
                metadata.FileState = FileState.Active;
            }

            await WriteFileHeaderInternal(header);

            //clean up temp storage
            await GetTempStorageManager(targetFile.DriveId).EnsureDeleted(targetFile.FileId);

            //HACKed in for Feed drive
            if (raiseEvent)
            {
                if (await ShouldRaiseDriveEvent(targetFile))
                {
                    if (wasAnUpdate)
                    {
                        await _mediator.Publish(new DriveFileAddedNotification()
                        {
                            File = targetFile,
                            ServerFileHeader = header,
                        });
                    }
                    else
                    {
                        await _mediator.Publish(new DriveFileChangedNotification()
                        {
                            File = targetFile,
                            ServerFileHeader = header,
                        });
                    }
                }
            }
        }

        public Task<uint> WriteTempStream(InternalDriveFileId file, string extension, Stream stream)
        {
            AssertCanWriteToDrive(file.DriveId);
            return GetTempStorageManager(file.DriveId).WriteStream(file.FileId, extension, stream);
        }

        public Task<Stream> GetTempStream(InternalDriveFileId file, string extension)
        {
            this.AssertCanReadDrive(file.DriveId);

            return GetTempStorageManager(file.DriveId).GetStream(file.FileId, extension);
        }

        public Task<Stream> GetTempStreamForWriting(InternalDriveFileId file, string extension)
        {
            this.AssertCanWriteToDrive(file.DriveId);

            return GetTempStorageManager(file.DriveId).GetStream(file.FileId, extension);
        }

        public Task DeleteTempFile(InternalDriveFileId file, string extension)
        {
            AssertCanWriteToDrive(file.DriveId);
            return GetTempStorageManager(file.DriveId).EnsureDeleted(file.FileId, extension);
        }

        public Task DeleteTempFiles(InternalDriveFileId file)
        {
            AssertCanWriteToDrive(file.DriveId);

            return GetTempStorageManager(file.DriveId).EnsureDeleted(file.FileId);
        }

        public Task<IEnumerable<ServerFileHeader>> GetMetadataFiles(Guid driveId, PageOptions pageOptions)
        {
            AssertCanReadDrive(driveId);

            return GetLongTermStorageManager(driveId).GetServerFileHeaders(pageOptions);
        }

        public async Task<(Stream stream, ThumbnailDescriptor thumbnail)> GetThumbnailPayloadStream(InternalDriveFileId file, int width, int height,
            string payloadKey, bool directMatchOnly = false)
        {
            this.AssertCanReadDrive(file.DriveId);

            DriveFileUtility.AssertValidPayloadKey(payloadKey);

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
                return (await GetLongTermStorageManager(file.DriveId).GetThumbnail(file.FileId, width, height, payloadKey), directMatchingThumb);
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

            return (
                await GetLongTermStorageManager(file.DriveId).GetThumbnail(file.FileId, nextSizeUp.PixelWidth, nextSizeUp.PixelHeight, payloadKey),
                nextSizeUp);
        }


        public async Task<Guid> DeletePayload(InternalDriveFileId file, string key, Guid versionTag)
        {
            this.AssertCanWriteToDrive(file.DriveId);

            //Note: calling to get the file header so we can ensure the caller can read this file
            var header = await this.GetServerFileHeader(file);
            DriveFileUtility.AssertVersionTagMatch(header.FileMetadata.VersionTag, versionTag);

            var descriptorIndex = header.FileMetadata.Payloads?.FindIndex(p => string.Equals(p.Key, key, StringComparison.InvariantCultureIgnoreCase)) ?? -1;

            if (descriptorIndex == -1)
            {
                return Guid.Empty;
            }

            var lts = GetLongTermStorageManager(file.DriveId);
            var descriptor = header.FileMetadata.Payloads![descriptorIndex];

            // Delete the thumbnail files for this payload
            foreach (var thumb in descriptor.Thumbnails ?? new List<ThumbnailDescriptor>())
            {
                await lts.DeleteThumbnailFile(file.FileId, key, thumb.PixelWidth, thumb.PixelHeight);
            }

            // Delete the payload file
            await lts.DeletePayloadFile(file.FileId, key);

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
            var header = await GetLongTermStorageManager(file.DriveId).GetServerFileHeader(file.FileId);

            if (null == header)
            {
                return false;
            }

            return await _driveAclAuthorizationService.CallerHasPermission(header.ServerMetadata.AccessControlList);
        }

        public async Task<ServerFileHeader> GetServerFileHeader(InternalDriveFileId file)
        {
            this.AssertCanReadDrive(file.DriveId);
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
            this.AssertCanReadOrWriteToDrive(file.DriveId);

            var header = await GetServerFileHeaderInternal(file);
            return header.ServerMetadata.FileSystemType;
        }

        public async Task<PayloadStream> GetPayloadStream(InternalDriveFileId file, string key, FileChunk chunk)
        {
            this.AssertCanReadDrive(file.DriveId);
            DriveFileUtility.AssertValidPayloadKey(key);

            //Note: calling to get the file header will also
            //ensure the caller can touch this file.
            var header = await this.GetServerFileHeader(file);
            if (header == null)
            {
                return null;
            }

            var descriptor = header.FileMetadata.GetPayloadDescriptor(key);

            if (descriptor == null)
            {
                return null;
            }

            var stream = await GetLongTermStorageManager(file.DriveId).GetPayloadStream(file.FileId, key, chunk);
            return new PayloadStream(descriptor, stream);
        }

        public void AssertFileIsValid(InternalDriveFileId file)
        {
            GetLongTermStorageManager(file.DriveId).AssertFileIsValid(file.FileId);
        }

        public bool FileExists(InternalDriveFileId file)
        {
            this.AssertCanReadOrWriteToDrive(file.DriveId);
            return GetLongTermStorageManager(file.DriveId).FileExists(file.FileId);
        }

        public async Task SoftDeleteLongTermFile(InternalDriveFileId file)
        {
            AssertCanWriteToDrive(file.DriveId);

            var existingHeader = await this.GetServerFileHeader(file);

            await WriteDeletedFileHeader(existingHeader);
        }

        public Task HardDeleteLongTermFile(InternalDriveFileId file)
        {
            AssertCanWriteToDrive(file.DriveId);

            var result = GetLongTermStorageManager(file.DriveId).HardDelete(file.FileId);

            if (ShouldRaiseDriveEvent(file).GetAwaiter().GetResult())
            {
                _mediator.Publish(new DriveFileDeletedNotification()
                {
                    IsHardDelete = true,
                    File = file,
                    ServerFileHeader = null,
                    SharedSecretEncryptedFileHeader = null
                });
            }

            return result;
        }

        public async Task CommitNewFile(InternalDriveFileId targetFile, KeyHeader keyHeader, FileMetadata metadata, ServerMetadata serverMetadata,
            bool? ignorePayload)
        {
            AssertCanWriteToDrive(targetFile.DriveId);

            metadata.File = targetFile;
            serverMetadata.FileSystemType = GetFileSystemType();

            var storageManager = GetLongTermStorageManager(targetFile.DriveId);
            var tempStorageManager = GetTempStorageManager(targetFile.DriveId);

            //HACK: To the transit system sending the file header and not the payload or thumbnails (via SendContents)
            // ignorePayload and ignoreThumbnail allow it to tell us what to expect.

            bool metadataSaysThisFileHasPayloads = metadata.Payloads?.Any() ?? false;

            if (metadataSaysThisFileHasPayloads && !ignorePayload.GetValueOrDefault(false))
            {
                //TODO: update payload size to be a sum of all payloads
                foreach (var descriptor in metadata.Payloads)
                {
                    //Note: it's just as performant to directly get the file length as it is to perform File.Exists
                    var payloadExtension = DriveFileUtility.GetPayloadFileExtension(descriptor.Key);
                    string sourceFile = await tempStorageManager.GetPath(targetFile.FileId, payloadExtension);
                    await storageManager.MovePayloadToLongTerm(targetFile.FileId, descriptor.Key, sourceFile);

                    string payloadKey = descriptor.Key;

                    foreach (var thumb in descriptor.Thumbnails ?? new List<ThumbnailDescriptor>())
                    {
                        var extension = DriveFileUtility.GetThumbnailFileExtension(thumb.PixelWidth, thumb.PixelHeight, payloadKey);
                        var sourceThumbnail = await tempStorageManager.GetPath(targetFile.FileId, extension);
                        await storageManager.MoveThumbnailToLongTerm(targetFile.FileId, sourceThumbnail, payloadKey, thumb);
                    }
                }
            }


            //TODO: calculate payload checksum, put on file metadata
            var serverHeader = await CreateServerHeaderInternal(targetFile, keyHeader, metadata, serverMetadata);

            await this.UpdateActiveFileHeader(targetFile, serverHeader);

            //clean up temp storage
            await tempStorageManager.EnsureDeleted(targetFile.FileId);

            if (await ShouldRaiseDriveEvent(targetFile))
            {
                await _mediator.Publish(new DriveFileAddedNotification()
                {
                    File = targetFile,
                    ServerFileHeader = serverHeader,
                });
            }
        }

        public async Task OverwriteFile(InternalDriveFileId tempFile, InternalDriveFileId targetFile, KeyHeader keyHeader, FileMetadata newMetadata,
            ServerMetadata serverMetadata, bool? ignorePayload)
        {
            AssertCanWriteToDrive(targetFile.DriveId);

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

            newMetadata.Created = existingServerHeader.FileMetadata.Created;
            newMetadata.GlobalTransitId = existingServerHeader.FileMetadata.GlobalTransitId;
            newMetadata.FileState = existingServerHeader.FileMetadata.FileState;

            newMetadata.File = targetFile;
            //Note: our call to GetServerFileHeader earlier validates the existing
            serverMetadata.FileSystemType = existingServerHeader.ServerMetadata.FileSystemType;

            var storageManager = GetLongTermStorageManager(targetFile.DriveId);
            var tempStorageManager = GetTempStorageManager(tempFile.DriveId);

            //HACK: To support the transit system sending the file header and not the payload or thumbnails (via SendContents)
            // ignorePayload and ignoreThumbnail allow it to tell us what to expect.

            bool metadataSaysThisFileHasPayloads = newMetadata.Payloads?.Any() ?? false;

            await storageManager.DeleteMissingPayloads(newMetadata.File.FileId, newMetadata.Payloads);

            if (metadataSaysThisFileHasPayloads && !ignorePayload.GetValueOrDefault(false))
            {
                foreach (var descriptor in newMetadata.Payloads)
                {
                    //Note: it's just as performant to directly get the file length as it is to perform File.Exists
                    var payloadExtension = DriveFileUtility.GetPayloadFileExtension(descriptor.Key);
                    string sourceFile = await tempStorageManager.GetPath(tempFile.FileId, payloadExtension);

                    await storageManager.MovePayloadToLongTerm(targetFile.FileId, descriptor.Key, sourceFile);

                    // Process thumbnails
                    var thumbs = descriptor.Thumbnails;
                    await storageManager.DeleteMissingThumbnailFiles(targetFile.FileId, thumbs);
                    foreach (var thumb in thumbs)
                    {
                        var extension = DriveFileUtility.GetThumbnailFileExtension(thumb.PixelWidth, thumb.PixelHeight, descriptor.Key);
                        var sourceThumbnail = await tempStorageManager.GetPath(tempFile.FileId, extension);
                        await storageManager.MoveThumbnailToLongTerm(targetFile.FileId, sourceThumbnail, descriptor.Key, thumb);
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
                await _mediator.Publish(new DriveFileChangedNotification()
                {
                    File = targetFile,
                    ServerFileHeader = serverHeader,
                });
            }
        }

        public async Task<Guid> UpdatePayloads(InternalDriveFileId sourceFile,
            InternalDriveFileId targetFile,
            List<PayloadDescriptor> incomingPayloads)
        {
            AssertCanWriteToDrive(targetFile.DriveId);

            var existingServerHeader = await this.GetServerFileHeader(targetFile);
            if (null == existingServerHeader)
            {
                throw new OdinClientException("Invalid target file", OdinClientErrorCode.FileNotFound);
            }

            if (existingServerHeader.FileMetadata.FileState != FileState.Active)
            {
                throw new OdinClientException("Cannot update a non-active file", OdinClientErrorCode.CannotUpdateNonActiveFile);
            }

            var storageManager = GetLongTermStorageManager(targetFile.DriveId);
            var tempStorageManager = GetTempStorageManager(sourceFile.DriveId);

            //Note: we do not delete existing payloads.  this feature adds or overwrites existing ones
            foreach (var descriptor in incomingPayloads)
            {
                // if the payload exists by key, overwrite; if not write new payload

                string extenstion = DriveFileUtility.GetPayloadFileExtension(descriptor.Key);

                string sourceFilePath = await tempStorageManager.GetPath(sourceFile.FileId, extenstion);
                await storageManager.MovePayloadToLongTerm(targetFile.FileId, descriptor.Key, sourceFilePath);

                // Delete any thumbnail that are no longer in the descriptor.Thumbnails from disk
                await storageManager.DeleteMissingThumbnailFiles(targetFile.FileId, descriptor.Thumbnails); //clean up

                foreach (var thumb in descriptor.Thumbnails ?? new List<ThumbnailDescriptor>())
                {
                    var extension = DriveFileUtility.GetThumbnailFileExtension(thumb.PixelWidth, thumb.PixelHeight, descriptor.Key);
                    var sourceThumbnail = await tempStorageManager.GetPath(sourceFile.FileId, extension);
                    await storageManager.MoveThumbnailToLongTerm(targetFile.FileId, sourceThumbnail, descriptor.Key, thumb);
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
                await _mediator.Publish(new DriveFileChangedNotification()
                {
                    File = targetFile,
                    ServerFileHeader = existingServerHeader,
                });
            }

            return existingServerHeader.FileMetadata.VersionTag.GetValueOrDefault();
        }

        public async Task OverwriteMetadata(InternalDriveFileId targetFile, FileMetadata newMetadata, ServerMetadata newServerMetadata)
        {
            AssertCanWriteToDrive(targetFile.DriveId);

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

            newServerMetadata.FileSystemType = existingServerHeader.ServerMetadata.FileSystemType;

            existingServerHeader.FileMetadata = newMetadata;
            existingServerHeader.ServerMetadata = newServerMetadata;

            await WriteFileHeaderInternal(existingServerHeader);

            //clean up temp storage
            await GetTempStorageManager(targetFile.DriveId).EnsureDeleted(targetFile.FileId);

            if (await ShouldRaiseDriveEvent(targetFile))
            {
                await _mediator.Publish(new DriveFileChangedNotification()
                {
                    File = targetFile,
                    ServerFileHeader = existingServerHeader,
                });
            }
        }

        public async Task UpdateReactionPreview(InternalDriveFileId targetFile, ReactionSummary summary)
        {
            ContextAccessor.GetCurrent().PermissionsContext
                .AssertHasAtLeastOneDrivePermission(targetFile.DriveId, DrivePermission.React, DrivePermission.Comment);
            var existingHeader = await GetLongTermStorageManager(targetFile.DriveId).GetServerFileHeader(targetFile.FileId);
            existingHeader.FileMetadata.ReactionPreview = summary;
            await WriteFileHeaderInternal(existingHeader);

            //clean up temp storage
            await GetTempStorageManager(targetFile.DriveId).EnsureDeleted(targetFile.FileId);

            if (await ShouldRaiseDriveEvent(targetFile))
            {
                await _mediator.Publish(new ReactionPreviewUpdatedNotification()
                {
                    File = targetFile,
                    ServerFileHeader = existingHeader,
                    SharedSecretEncryptedFileHeader = DriveFileUtility.ConvertToSharedSecretEncryptedClientFileHeader(existingHeader, ContextAccessor)
                });
            }
        }

        // Feed drive hacks

        public async Task WriteNewFileToFeedDrive(KeyHeader keyHeader, FileMetadata fileMetadata)
        {
            // Method assumes you ensured the file was unique by some other method

            var feedDriveId = await _driveManager.GetDriveIdByAlias(SystemDriveConstants.FeedDrive);
            this.AssertCanWriteToDrive(feedDriveId.GetValueOrDefault());
            var file = this.CreateInternalFileId(feedDriveId.GetValueOrDefault());

            var serverMetadata = new ServerMetadata()
            {
                AccessControlList = AccessControlList.OwnerOnly,
                AllowDistribution = false
            };

            var serverFileHeader = await this.CreateServerFileHeader(file, keyHeader, fileMetadata, serverMetadata);

            await this.UpdateActiveFileHeader(file, serverFileHeader, raiseEvent: true);
        }

        public async Task ReplaceFileMetadataOnFeedDrive(InternalDriveFileId file, FileMetadata fileMetadata)
        {
            this.AssertCanWriteToDrive(file.DriveId);
            var header = await GetServerFileHeaderInternal(file);
            AssertValidFileSystemType(header.ServerMetadata);
            var feedDriveId = await _driveManager.GetDriveIdByAlias(SystemDriveConstants.FeedDrive);

            if (file.DriveId != feedDriveId)
            {
                throw new OdinSystemException("Method cannot be used on drive");
            }

            //S0510
            if (header.FileMetadata.SenderOdinId != ContextAccessor.GetCurrent().GetCallerOdinIdOrFail())
            {
                throw new OdinSecurityException("Invalid caller");
            }

            header.FileMetadata = fileMetadata;

            await this.UpdateActiveFileHeader(file, header, raiseEvent: true);
        }

        public async Task RemoveFeedDriveFile(InternalDriveFileId file)
        {
            this.AssertCanWriteToDrive(file.DriveId);
            var header = await GetServerFileHeaderInternal(file);
            AssertValidFileSystemType(header.ServerMetadata);
            var feedDriveId = await _driveManager.GetDriveIdByAlias(SystemDriveConstants.FeedDrive);

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
            AssertCanWriteToDrive(targetFile.DriveId);
            var feedDriveId = await _driveManager.GetDriveIdByAlias(SystemDriveConstants.FeedDrive);
            if (targetFile.DriveId != feedDriveId)
            {
                throw new OdinSystemException("Cannot update reaction preview on this drive");
            }

            var existingHeader = await GetLongTermStorageManager(targetFile.DriveId).GetServerFileHeader(targetFile.FileId);

            //S0510
            if (existingHeader.FileMetadata.SenderOdinId != ContextAccessor.GetCurrent().Caller.OdinId)
            {
                throw new OdinSecurityException("Invalid caller");
            }

            existingHeader.FileMetadata.ReactionPreview = summary;
            await WriteFileHeaderInternal(existingHeader);

            //clean up temp storage
            await GetTempStorageManager(targetFile.DriveId).EnsureDeleted(targetFile.FileId);

            if (await ShouldRaiseDriveEvent(targetFile))
            {
                await _mediator.Publish(new ReactionPreviewUpdatedNotification()
                {
                    File = targetFile,
                    ServerFileHeader = existingHeader,
                    SharedSecretEncryptedFileHeader = DriveFileUtility.ConvertToSharedSecretEncryptedClientFileHeader(existingHeader, ContextAccessor)
                });
            }
        }

        private LongTermStorageManager GetLongTermStorageManager(Guid driveId)
        {
            var logger = _loggerFactory.CreateLogger<LongTermStorageManager>();
            var drive = this.DriveManager.GetDrive(driveId, failIfInvalid: true).GetAwaiter().GetResult();
            var manager = new LongTermStorageManager(drive, logger);
            return manager;
        }

        private TempStorageManager GetTempStorageManager(Guid driveId)
        {
            var drive = this.DriveManager.GetDrive(driveId, failIfInvalid: true).GetAwaiter().GetResult();
            var logger = _loggerFactory.CreateLogger<TempStorageManager>();
            return new TempStorageManager(drive, logger);
        }

        private async Task WriteFileHeaderInternal(ServerFileHeader header)
        {
            header.FileMetadata.VersionTag = SequentialGuid.CreateGuid();
            header.FileMetadata.Updated = UnixTimeUtc.Now().milliseconds;

            var file = header.FileMetadata.File;
            var payloadDiskUsage = GetLongTermStorageManager(file.DriveId).GetPayloadDiskUsage(file.FileId);

            var json = OdinSystemSerializer.Serialize(header);
            header.ServerMetadata.FileByteCount = payloadDiskUsage + Encoding.UTF8.GetBytes(json).Length;

            json = OdinSystemSerializer.Serialize(header);
            var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));

            await GetLongTermStorageManager(header.FileMetadata.File.DriveId).WritePartStream(header.FileMetadata.File.FileId, FilePart.Header, stream);
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
            return file.DriveId != (await _driveManager.GetDriveIdByAlias(SystemDriveConstants.TransientTempDrive));
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

            await GetLongTermStorageManager(file.DriveId).DeleteAttachments(file.FileId);
            await this.WriteFileHeaderInternal(deletedServerFileHeader);

            if (await ShouldRaiseDriveEvent(file))
            {
                await _mediator.Publish(new DriveFileDeletedNotification()
                {
                    PreviousServerFileHeader = existingHeader,
                    IsHardDelete = false,
                    File = file,
                    ServerFileHeader = deletedServerFileHeader,
                    SharedSecretEncryptedFileHeader = DriveFileUtility.ConvertToSharedSecretEncryptedClientFileHeader(deletedServerFileHeader, ContextAccessor)
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
            var header = await GetLongTermStorageManager(file.DriveId).GetServerFileHeader(file.FileId);

            if (null == header)
            {
                return null;
            }

            await _driveAclAuthorizationService.AssertCallerHasPermission(header.ServerMetadata.AccessControlList);

            return header;
        }
    }
}