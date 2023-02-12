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
using Youverse.Core.Serialization;
using Youverse.Core.Services.Authorization.Acl;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Drive.Core.Storage;
using Youverse.Core.Services.Mediator;
using Youverse.Core.Services.Transit.Encryption;
using Youverse.Core.Storage;

namespace Youverse.Core.Services.Drive
{
    // Note: drive storage using the ThreeKey KeyValueDatabase
    // key1 = drive id
    // key2 = drive type  + drive alias (see TargetDrive.ToKey() method)
    // key3 = type of data identifier (the fact this is a drive; note: we should put datatype on the KV database)

    public abstract class DriveServiceBase : IDriveService
    {
        private readonly DriveManager _driveManager;
        private readonly IDriveAclAuthorizationService _driveAclAuthorizationService;
        private readonly IMediator _mediator;
        private readonly DotYouContextAccessor _contextAccessor;
        private readonly TenantContext _tenantContext;
        private readonly ILoggerFactory _loggerFactory;

        protected DriveServiceBase(DotYouContextAccessor contextAccessor, ITenantSystemStorage tenantSystemStorage, ILoggerFactory loggerFactory, IMediator mediator,
            IDriveAclAuthorizationService driveAclAuthorizationService, TenantContext tenantContext)
        {
            _contextAccessor = contextAccessor;

            _loggerFactory = loggerFactory;
            _mediator = mediator;
            _driveAclAuthorizationService = driveAclAuthorizationService;
            _tenantContext = tenantContext;
            _driveManager = new DriveManager(contextAccessor, tenantSystemStorage, mediator, tenantContext);
        }

        protected DotYouContextAccessor ContextAccessor => _contextAccessor;

        public Task<StorageDrive> CreateDrive(CreateDriveRequest request)
        {
            return _driveManager.CreateDrive(request);
        }

        public Task SetDriveReadMode(Guid driveId, bool allowAnonymous)
        {
            return _driveManager.SetDriveReadMode(driveId, allowAnonymous);
        }

        public Task UpdateMetadata(Guid driveId, string metadata)
        {
            return _driveManager.UpdateMetadata(driveId, metadata);
        }

        public async Task<StorageDrive> GetDrive(Guid driveId, bool failIfInvalid = false)
        {
            return await _driveManager.GetDrive(driveId, failIfInvalid);
        }

        public async Task<Guid?> GetDriveIdByAlias(TargetDrive targetDrive, bool failIfInvalid = false)
        {
            return await _driveManager.GetDriveIdByAlias(targetDrive, failIfInvalid);
        }

        public async Task<PagedResult<StorageDrive>> GetDrives(PageOptions pageOptions)
        {
            return await _driveManager.GetDrives(pageOptions);
        }

        public async Task<PagedResult<StorageDrive>> GetDrives(GuidId type, PageOptions pageOptions)
        {
            return await _driveManager.GetDrives(type, pageOptions);
        }

        public async Task<PagedResult<StorageDrive>> GetAnonymousDrives(PageOptions pageOptions)
        {
            return await _driveManager.GetAnonymousDrives(pageOptions);
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

        public async Task UpdateActiveFileHeader(InternalDriveFileId file, ServerFileHeader header)
        {
            Guard.Argument(header, nameof(header)).NotNull();
            Guard.Argument(header, nameof(header)).Require(x => x.IsValid());

            AssertCanWriteToDrive(file.DriveId);

            var metadata = header.FileMetadata;

            //TODO: need to encrypt the metadata parts
            metadata.File = file; //TBH it's strange having this but we need the metadata to have the file and drive embedded

            if (this.FileExists(file))
            {
                var existingHeader = await this.GetServerFileHeader(file);
                metadata.Updated = UnixTimeUtc.Now().milliseconds;
                metadata.Created = existingHeader.FileMetadata.Created;
                metadata.GlobalTransitId = existingHeader.FileMetadata.GlobalTransitId;

                if (metadata.FileState == FileState.Deleted)
                {
                    throw new YouverseClientException("Cannot update deleted file", YouverseClientErrorCode.CannotUpdateDeletedFile);
                }

                //TODO: determine if we need to see if it was previously deleted, then call this an error
                metadata.FileState = FileState.Active;
            }
            else
            {
                metadata.Created = UnixTimeUtc.Now().milliseconds;
                metadata.FileState = FileState.Active;
            }

            await WriteFileHeaderInternal(header);
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

        public async Task<ServerFileHeader> CreateServerFileHeader(InternalDriveFileId file, KeyHeader keyHeader, FileMetadata fileMetadata, ServerMetadata serverMetadata)
        {
            var sv = new ServerFileHeader()
            {
                EncryptedKeyHeader = await this.EncryptKeyHeader(file.DriveId, keyHeader),
                FileMetadata = fileMetadata,
                ServerMetadata = serverMetadata
            };

            return sv;
        }

        private async Task<EncryptedKeyHeader> EncryptKeyHeader(Guid driveId, KeyHeader keyHeader)
        {
            var storageKey = _contextAccessor.GetCurrent().PermissionsContext.GetDriveStorageKey(driveId);

            (await this.GetDrive(driveId)).AssertValidStorageKey(storageKey);

            var encryptedKeyHeader = EncryptedKeyHeader.EncryptKeyHeaderAes(keyHeader, keyHeader.Iv, ref storageKey);
            return encryptedKeyHeader;
        }

        public async Task<ServerFileHeader> GetServerFileHeader(InternalDriveFileId file)
        {
            this.AssertCanReadDrive(file.DriveId);

            var header = await GetLongTermStorageManager(file.DriveId).GetServerFileHeader(file.FileId);

            if (null == header)
            {
                return null;
            }

            await _driveAclAuthorizationService.AssertCallerHasPermission(header.ServerMetadata.AccessControlList);

            return header;
        }

        public async Task<Stream> GetPayloadStream(InternalDriveFileId file)
        {
            this.AssertCanReadDrive(file.DriveId);

            //Note: calling to get the file header so we can ensure the caller can read this file

            var header = await this.GetServerFileHeader(file);
            if (header.FileMetadata.AppData.ContentIsComplete == false)
            {
                var stream = await GetLongTermStorageManager(file.DriveId).GetFilePartStream(file.FileId, FilePart.Payload);
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
            this.AssertCanReadDrive(file.DriveId);
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
        }

        public Task HardDeleteLongTermFile(InternalDriveFileId file)
        {
            AssertCanWriteToDrive(file.DriveId);

            var result = GetLongTermStorageManager(file.DriveId).HardDelete(file.FileId);

            var notification = new DriveFileDeletedNotification()
            {
                File = file
            };

            _mediator.Publish(notification);

            return result;
        }

        public async Task CommitNewFile(InternalDriveFileId targetFile, KeyHeader keyHeader, FileMetadata metadata, ServerMetadata serverMetadata, string payloadExtension)
        {
            AssertCanWriteToDrive(targetFile.DriveId);

            metadata.File = targetFile;

            var storageManager = GetLongTermStorageManager(targetFile.DriveId);
            var tempStorageManager = GetTempStorageManager(targetFile.DriveId);

            if (metadata.AppData.ContentIsComplete == false)
            {
                string sourceFile = await tempStorageManager.GetPath(targetFile.FileId, payloadExtension);
                metadata.PayloadSize = new FileInfo(sourceFile).Length;
                await storageManager.MoveToLongTerm(targetFile.FileId, sourceFile, FilePart.Payload);
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
            var serverHeader = new ServerFileHeader()
            {
                EncryptedKeyHeader = await this.EncryptKeyHeader(targetFile.DriveId, keyHeader),
                FileMetadata = metadata,
                ServerMetadata = serverMetadata
            };

            //Note: calling write metadata last since it will call OnLongTermFileChanged to ensure it is indexed
            await this.UpdateActiveFileHeader(targetFile, serverHeader);

            await _mediator.Publish(new DriveFileAddedNotification()
            {
                File = targetFile,
                ServerFileHeader = serverHeader
            });
        }

        public async Task OverwriteFile(InternalDriveFileId tempFile, InternalDriveFileId targetFile, KeyHeader keyHeader, FileMetadata metadata, ServerMetadata serverMetadata,
            string payloadExtension)
        {
            AssertCanWriteToDrive(targetFile.DriveId);

            metadata.File = targetFile;

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

            //Note: calling write metadata last since it will call OnLongTermFileChanged to ensure it is indexed
            await this.UpdateActiveFileHeader(targetFile, serverHeader);

            await _mediator.Publish(new DriveFileChangedNotification()
            {
                File = targetFile,
                ServerFileHeader = serverHeader
            });
        }


        //

        /// <summary>
        /// Enforces drive permissions when reading files
        /// </summary>
        protected abstract void AssertCanReadDrive(Guid driveId);

        /// <summary>
        /// Enforces drive permissions when writing files
        /// </summary>
        protected abstract void AssertCanWriteToDrive(Guid driveId);

        //
        private ILongTermStorageManager GetLongTermStorageManager(Guid driveId)
        {
            var logger = _loggerFactory.CreateLogger<ILongTermStorageManager>();
            var drive = this.GetDrive(driveId, failIfInvalid: true).GetAwaiter().GetResult();
            var manager = new FileBasedLongTermStorageManager(drive, logger);
            return manager;
        }

        private ITempStorageManager GetTempStorageManager(Guid driveId)
        {
            var drive = this.GetDrive(driveId, failIfInvalid: true).GetAwaiter().GetResult();
            var logger = _loggerFactory.CreateLogger<ITempStorageManager>();
            return new FileBasedTempStorageManager(drive, logger);
        }

        private async Task WriteFileHeaderInternal(ServerFileHeader header)
        {
            var json = DotYouSystemSerializer.Serialize(header);
            var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
            await GetLongTermStorageManager(header.FileMetadata.File.DriveId).WritePartStream(header.FileMetadata.File.FileId, FilePart.Header, stream);
        }
    }
}