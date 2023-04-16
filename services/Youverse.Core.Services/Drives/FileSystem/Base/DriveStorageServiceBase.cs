using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Dawn;
using MediatR;
using Microsoft.Extensions.Logging;
using Youverse.Core.Exceptions;
using Youverse.Core.Identity;
using Youverse.Core.Serialization;
using Youverse.Core.Services.Apps;
using Youverse.Core.Services.Authorization.Acl;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Drives.DriveCore.Storage;
using Youverse.Core.Services.Drives.Management;
using Youverse.Core.Services.Mediator;
using Youverse.Core.Services.Transit;
using Youverse.Core.Services.Transit.Encryption;
using Youverse.Core.Services.Transit.ReceivingHost;
using Youverse.Core.Storage;

namespace Youverse.Core.Services.Drives.FileSystem.Base
{
    public abstract class DriveStorageServiceBase : RequirePermissionsBase
    {
        private readonly IDriveAclAuthorizationService _driveAclAuthorizationService;
        private readonly IMediator _mediator;
        private readonly ILoggerFactory _loggerFactory;
        private readonly DriveManager _driveManager;

        protected DriveStorageServiceBase(
            DotYouContextAccessor contextAccessor,
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
        protected override DotYouContextAccessor ContextAccessor { get; }

        /// <summary>
        /// Gets the <see cref="FileSystemType"/> the inheriting class manages
        /// </summary>
        public abstract FileSystemType GetFileSystemType();

        public async Task<SharedSecretEncryptedFileHeader> GetSharedSecretEncryptedHeader(InternalDriveFileId file)
        {
            var serverFileHeader = await this.GetServerFileHeader(file);
            var result = Utility.ConvertToSharedSecretEncryptedClientFileHeader(serverFileHeader, ContextAccessor);
            return result;
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

        public async Task UpdateActiveFileHeader(InternalDriveFileId file, ServerFileHeader header, bool raiseEvent = false)
        {
            Guard.Argument(header, nameof(header)).NotNull();
            Guard.Argument(header, nameof(header)).Require(x => x.IsValid());

            AssertCanWriteToDrive(file.DriveId);

            var metadata = header.FileMetadata;

            //TODO: need to encrypt the metadata parts
            metadata.File = file; //TBH it's strange having this but we need the metadata to have the file and drive embedded

            bool wasAnUpdate = false;
            if (this.FileExists(file))
            {
                if (metadata.FileState != FileState.Active)
                {
                    throw new YouverseClientException("Cannot update non-active file", YouverseClientErrorCode.CannotUpdateNonActiveFile);
                }

                var existingHeader = await this.GetServerFileHeaderInternal(file);
                metadata.Updated = UnixTimeUtc.Now().milliseconds;
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

            //HACKed in for Feed drive
            if (raiseEvent)
            {
                if (await ShouldRaiseDriveEvent(file))
                {
                    if (wasAnUpdate)
                    {
                        await _mediator.Publish(new DriveFileAddedNotification()
                        {
                            File = file,
                            ServerFileHeader = header,
                            SharedSecretEncryptedFileHeader = Utility.ConvertToSharedSecretEncryptedClientFileHeader(header, ContextAccessor)
                        });
                    }
                    else
                    {
                        await _mediator.Publish(new DriveFileChangedNotification()
                        {
                            File = file,
                            ServerFileHeader = header,
                            SharedSecretEncryptedFileHeader = Utility.ConvertToSharedSecretEncryptedClientFileHeader(header, ContextAccessor)
                        });
                    }
                }
            }
        }

        public Task WritePartStream(InternalDriveFileId file, FilePart filePart, Stream stream)
        {
            AssertCanWriteToDrive(file.DriveId);

            var task = GetLongTermStorageManager(file.DriveId).WritePartStream(file.FileId, filePart, stream);
            return task;
        }

        public async Task<T> GetDeserializedStream<T>(InternalDriveFileId file, string extension, StorageDisposition disposition = StorageDisposition.LongTerm)
        {
            this.AssertCanReadDrive(file.DriveId);

            if (disposition == StorageDisposition.LongTerm)
            {
                throw new NotImplementedException("Not supported for long term storage");
            }

            var stream = await this.GetTempStream(file, extension);
            string json = await new StreamReader(stream).ReadToEndAsync();
            var o = DotYouSystemSerializer.Deserialize<T>(json);
            return o;
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

            return GetTempStorageManager(file.DriveId).Delete(file.FileId, extension);
        }

        public Task DeleteTempFiles(InternalDriveFileId file)
        {
            AssertCanWriteToDrive(file.DriveId);

            return GetTempStorageManager(file.DriveId).Delete(file.FileId);
        }

        public Task<IEnumerable<ServerFileHeader>> GetMetadataFiles(Guid driveId, PageOptions pageOptions)
        {
            AssertCanReadDrive(driveId);

            return GetLongTermStorageManager(driveId).GetServerFileHeaders(pageOptions);
        }

        public async Task<Stream> GetThumbnailPayloadStream(InternalDriveFileId file, int width, int height)
        {
            this.AssertCanReadDrive(file.DriveId);

            //Note: calling to get the file header so we can ensure the caller can read this file
            var header = await this.GetServerFileHeader(file);
            var thumbs = header?.FileMetadata?.AppData?.AdditionalThumbnails?.ToList();
            if (null == thumbs || !thumbs.Any())
            {
                return Stream.Null;
            }

            var directMatchingThumb = thumbs.SingleOrDefault(t => t.PixelHeight == height && t.PixelWidth == width);
            if (null != directMatchingThumb)
            {
                return await GetLongTermStorageManager(file.DriveId).GetThumbnail(file.FileId, width, height);
            }

            //TODO: add more logic here to compare width and height separately or together
            var nextSizeUp = thumbs.FirstOrDefault(t => t.PixelHeight > height || t.PixelWidth > width);
            if (null == nextSizeUp)
            {
                nextSizeUp = thumbs.LastOrDefault();
                if (null == nextSizeUp)
                {
                    return Stream.Null;
                }
            }

            return await GetLongTermStorageManager(file.DriveId).GetThumbnail(file.FileId, nextSizeUp.PixelWidth, nextSizeUp.PixelHeight);
        }

        public async Task WriteThumbnailStream(InternalDriveFileId file, int width, int height, Stream stream)
        {
            AssertCanWriteToDrive(file.DriveId);
            await GetLongTermStorageManager(file.DriveId).WriteThumbnail(file.FileId, width, height, stream);
        }

        public string GetThumbnailFileExtension(int width, int height)
        {
            //TODO: move this down into the long term storage manager
            string extenstion = $"-{width}x{height}.thumb";
            return extenstion;
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

        public async Task<ServerFileHeader> GetServerFileHeader(InternalDriveFileId file)
        {
            this.AssertCanReadDrive(file.DriveId);
            var header = await GetServerFileHeaderInternal(file);
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

        public async Task<Stream> GetPayloadStream(InternalDriveFileId file, FileChunk chunk)
        {
            this.AssertCanReadDrive(file.DriveId);

            //Note: calling to get the file header so we can ensure the caller can read this file

            var header = await this.GetServerFileHeader(file);
            if (header.FileMetadata.AppData.ContentIsComplete == false)
            {
                var stream = await GetLongTermStorageManager(file.DriveId).GetFilePartStream(file.FileId, FilePart.Payload, chunk);
                return stream;
            }

            return Stream.Null;
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

            await this.WriteFileHeaderInternal(deletedServerFileHeader);
            await GetLongTermStorageManager(file.DriveId).SoftDelete(file.FileId);

            if (await ShouldRaiseDriveEvent(file))
            {
                await _mediator.Publish(new DriveFileDeletedNotification()
                {
                    IsHardDelete = false,
                    File = file,
                    ServerFileHeader = deletedServerFileHeader,
                    SharedSecretEncryptedFileHeader = Utility.ConvertToSharedSecretEncryptedClientFileHeader(deletedServerFileHeader, ContextAccessor)
                });
            }
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
            string payloadExtension)
        {
            AssertCanWriteToDrive(targetFile.DriveId);

            metadata.File = targetFile;
            serverMetadata.FileSystemType = GetFileSystemType();

            var storageManager = GetLongTermStorageManager(targetFile.DriveId);
            var tempStorageManager = GetTempStorageManager(targetFile.DriveId);

            //HACK: Note: It's possible for a transfer only include the header (excluding thumbnails and payloads)
            //in the case of TransitOptions.SendOptions only having the HeaderFlag
            //
            if (metadata.AppData.ContentIsComplete == false)
            {
                try
                {
                    string sourceFile = await tempStorageManager.GetPath(targetFile.FileId, payloadExtension);
                    metadata.PayloadSize = new FileInfo(sourceFile).Length;
                    await storageManager.MoveToLongTerm(targetFile.FileId, sourceFile, FilePart.Payload);
                }
                catch
                {
                    //HACK:  It's possible for a transfer only include the header (excluding thumbnails and payloads)
                    //in the case of TransitOptions.SendOptions only having the HeaderFlag
                }
            }

            if (metadata.AppData.AdditionalThumbnails != null)
            {
                foreach (var thumb in metadata.AppData.AdditionalThumbnails)
                {
                    var extension = this.GetThumbnailFileExtension(thumb.PixelWidth, thumb.PixelHeight);
                    var sourceThumbnail = await tempStorageManager.GetPath(targetFile.FileId, extension);
                    await storageManager.MoveThumbnailToLongTerm(targetFile.FileId, sourceThumbnail, thumb.PixelWidth, thumb.PixelHeight);
                }
            }

            //TODO: calculate payload checksum, put on file metadata
            var serverHeader = await CreateServerHeaderInternal(targetFile, keyHeader, metadata, serverMetadata);

            await this.UpdateActiveFileHeader(targetFile, serverHeader);

            if (await ShouldRaiseDriveEvent(targetFile))
            {
                await _mediator.Publish(new DriveFileAddedNotification()
                {
                    File = targetFile,
                    ServerFileHeader = serverHeader,
                    SharedSecretEncryptedFileHeader = Utility.ConvertToSharedSecretEncryptedClientFileHeader(serverHeader, ContextAccessor)
                });
            }
        }

        private async Task<ServerFileHeader> CreateServerHeaderInternal(InternalDriveFileId targetFile, KeyHeader keyHeader, FileMetadata metadata,
            ServerMetadata serverMetadata)
        {
            serverMetadata.FileSystemType = GetFileSystemType();

            return new ServerFileHeader()
            {
                EncryptedKeyHeader = metadata.PayloadIsEncrypted ? await this.EncryptKeyHeader(targetFile.DriveId, keyHeader) : EncryptedKeyHeader.Empty(),
                FileMetadata = metadata,
                ServerMetadata = serverMetadata
            };
        }

        public async Task OverwriteFile(InternalDriveFileId tempFile, InternalDriveFileId targetFile, KeyHeader keyHeader, FileMetadata metadata,
            ServerMetadata serverMetadata,
            string payloadExtension)
        {
            AssertCanWriteToDrive(targetFile.DriveId);

            var existingServerHeader = await this.GetServerFileHeader(targetFile);
            if (null == existingServerHeader)
            {
                throw new YouverseClientException("Cannot overwrite file that does not exist", YouverseClientErrorCode.FileNotFound);
            }

            if (existingServerHeader.FileMetadata.FileState != FileState.Active)
            {
                throw new YouverseClientException("Cannot update a non-active file", YouverseClientErrorCode.CannotUpdateNonActiveFile);
            }

            if (existingServerHeader.FileMetadata.ConcurrencyToken != metadata.ConcurrencyToken)
            {
                throw new YouverseClientException($"Invalid {metadata.ConcurrencyToken}", YouverseClientErrorCode.ConcurrencyTokenMismatch);
            }
            
            metadata.Updated = UnixTimeUtc.Now().milliseconds;
            metadata.Created = existingServerHeader.FileMetadata.Created;
            metadata.GlobalTransitId = existingServerHeader.FileMetadata.GlobalTransitId;
            metadata.FileState = existingServerHeader.FileMetadata.FileState;

            metadata.File = targetFile;
            //Note: our call to GetServerFileHeader earlier validates the existing
            serverMetadata.FileSystemType = existingServerHeader.ServerMetadata.FileSystemType;

            var storageManager = GetLongTermStorageManager(targetFile.DriveId);
            var tempStorageManager = GetTempStorageManager(tempFile.DriveId);

            if (metadata.AppData.ContentIsComplete == false)
            {
                string sourceFile = await tempStorageManager.GetPath(tempFile.FileId, payloadExtension);
                metadata.PayloadSize = new FileInfo(sourceFile).Length;
                await storageManager.MoveToLongTerm(targetFile.FileId, sourceFile, FilePart.Payload);
            }

            //TODO: clean up old payload if it was removed?

            var thumbs = metadata.AppData.AdditionalThumbnails?.ToList() ?? new List<ImageDataHeader>();
            await storageManager.ReconcileThumbnailsOnDisk(targetFile.FileId, thumbs);
            foreach (var thumb in thumbs)
            {
                var extension = this.GetThumbnailFileExtension(thumb.PixelWidth, thumb.PixelHeight);
                var sourceThumbnail = await tempStorageManager.GetPath(tempFile.FileId, extension);
                await storageManager.MoveThumbnailToLongTerm(targetFile.FileId, sourceThumbnail, thumb.PixelWidth, thumb.PixelHeight);
            }

            //TODO: calculate payload checksum, put on file metadata
            var serverHeader = new ServerFileHeader()
            {
                EncryptedKeyHeader = await this.EncryptKeyHeader(tempFile.DriveId, keyHeader),
                FileMetadata = metadata,
                ServerMetadata = serverMetadata
            };

            await WriteFileHeaderInternal(serverHeader);
            if (await ShouldRaiseDriveEvent(targetFile))
            {
                await _mediator.Publish(new DriveFileChangedNotification()
                {
                    File = targetFile,
                    ServerFileHeader = serverHeader,
                    SharedSecretEncryptedFileHeader = Utility.ConvertToSharedSecretEncryptedClientFileHeader(serverHeader, ContextAccessor)
                });
            }
        }


        public async Task UpdateReactionPreview(InternalDriveFileId targetFile, ReactionSummary summary)
        {
            ContextAccessor.GetCurrent().PermissionsContext.AssertHasDrivePermission(targetFile.DriveId, DrivePermission.WriteReactionsAndComments);

            var existingHeader = await GetLongTermStorageManager(targetFile.DriveId).GetServerFileHeader(targetFile.FileId);
            existingHeader.FileMetadata.ReactionPreview = summary;
            await WriteFileHeaderInternal(existingHeader);

            if (await ShouldRaiseDriveEvent(targetFile))
            {
                await _mediator.Publish(new ReactionPreviewUpdatedNotification()
                {
                    File = targetFile,
                    ServerFileHeader = existingHeader,
                    SharedSecretEncryptedFileHeader = Utility.ConvertToSharedSecretEncryptedClientFileHeader(existingHeader, ContextAccessor)
                });
            }
        }

        // Feed drive hacks
        public async Task ReplaceFileMetadataOnFeedDrive(InternalDriveFileId file, FileMetadata fileMetadata)
        {
            this.AssertCanWriteToDrive(file.DriveId);
            var header = await GetServerFileHeaderInternal(file);
            AssertValidFileSystemType(header.ServerMetadata);
            var feedDriveId = await _driveManager.GetDriveIdByAlias(SystemDriveConstants.FeedDrive);

            if (file.DriveId != feedDriveId)
            {
                throw new YouverseSystemException("Method cannot be used on drive");
            }

            //S0510
            if (header.FileMetadata.SenderOdinId != ContextAccessor.GetCurrent().GetCallerOdinIdOrFail())
            {
                throw new YouverseSecurityException("Invalid caller");
            }

            header.FileMetadata = fileMetadata;

            await this.UpdateActiveFileHeader(file, header, raiseEvent: true);
        }

        public async Task UpdateReactionPreviewOnFeedDrive(InternalDriveFileId targetFile, ReactionSummary summary)
        {
            AssertCanWriteToDrive(targetFile.DriveId);
            var feedDriveId = await _driveManager.GetDriveIdByAlias(SystemDriveConstants.FeedDrive);
            if (targetFile.DriveId != feedDriveId)
            {
                throw new YouverseSystemException("Cannot update reaction preview on this drive");
            }

            var existingHeader = await GetLongTermStorageManager(targetFile.DriveId).GetServerFileHeader(targetFile.FileId);

            //S0510
            if (existingHeader.FileMetadata.SenderOdinId != ContextAccessor.GetCurrent().Caller.OdinId)
            {
                throw new YouverseSecurityException("Invalid caller");
            }

            existingHeader.FileMetadata.ReactionPreview = summary;
            await WriteFileHeaderInternal(existingHeader);

            if (await ShouldRaiseDriveEvent(targetFile))
            {
                await _mediator.Publish(new ReactionPreviewUpdatedNotification()
                {
                    File = targetFile,
                    ServerFileHeader = existingHeader,
                    SharedSecretEncryptedFileHeader = Utility.ConvertToSharedSecretEncryptedClientFileHeader(existingHeader, ContextAccessor)
                });
            }
        }

        //
        private ILongTermStorageManager GetLongTermStorageManager(Guid driveId)
        {
            var logger = _loggerFactory.CreateLogger<ILongTermStorageManager>();
            var drive = this.DriveManager.GetDrive(driveId, failIfInvalid: true).GetAwaiter().GetResult();
            var manager = new FileBasedLongTermStorageManager(drive, logger);
            return manager;
        }

        private ITempStorageManager GetTempStorageManager(Guid driveId)
        {
            var drive = this.DriveManager.GetDrive(driveId, failIfInvalid: true).GetAwaiter().GetResult();
            var logger = _loggerFactory.CreateLogger<ITempStorageManager>();
            return new FileBasedTempStorageManager(drive, logger);
        }

        private async Task WriteFileHeaderInternal(ServerFileHeader header)
        {
            header.FileMetadata.ConcurrencyToken = SequentialGuid.CreateGuid();
            
            var json = DotYouSystemSerializer.Serialize(header);
            var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
            await GetLongTermStorageManager(header.FileMetadata.File.DriveId).WritePartStream(header.FileMetadata.File.FileId, FilePart.Header, stream);
        }

        /// <summary>
        /// Protects against using the wrong filesystem with a fileId
        /// </summary>
        /// <param name="serverMetadata"></param>
        /// <exception cref="YouverseClientException"></exception>
        private void AssertValidFileSystemType(ServerMetadata serverMetadata)
        {
            if (serverMetadata.FileSystemType != GetFileSystemType())
            {
                //just in case the caller used the wrong drive service instance
                throw new YouverseClientException($"Invalid SystemFileCategory.  This service only handles the FileSystemType of {GetFileSystemType()}");
            }
        }

        private async Task<bool> ShouldRaiseDriveEvent(InternalDriveFileId file)
        {
            return file.DriveId != (await _driveManager.GetDriveIdByAlias(SystemDriveConstants.TransientTempDrive));
        }
    }
}