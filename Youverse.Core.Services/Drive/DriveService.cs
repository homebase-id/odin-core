using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Dawn;
using MediatR;
using Microsoft.Extensions.Logging;
using Youverse.Core.Cryptography.Crypto;
using Youverse.Core.Cryptography.Data;
using Youverse.Core.Exceptions;
using Youverse.Core.Serialization;
using Youverse.Core.Services.Authorization.Acl;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Drive.Storage;
using Youverse.Core.Services.Mediator;
using Youverse.Core.Services.Transit.Encryption;
using Youverse.Core.Storage;

namespace Youverse.Core.Services.Drive
{
    // Note: drive storage using the ThreeKey KeyValueDatabase
    // key1 = drive id
    // key2 = drive type  + drive alias (see TargetDrive.ToKey() method)
    // key3 = type of data identifier (the fact this is a drive; note: we should put datatype on the KV database)

    public class DriveService : IDriveService
    {
        private readonly IDriveAclAuthorizationService _driveAclAuthorizationService;
        private readonly ISystemStorage _systemStorage;
        private readonly IMediator _mediator;
        private readonly DotYouContextAccessor _contextAccessor;
        private readonly TenantContext _tenantContext;
        private readonly ConcurrentDictionary<Guid, ILongTermStorageManager> _longTermStorageManagers;
        private readonly ConcurrentDictionary<Guid, ITempStorageManager> _tempStorageManagers;
        private readonly ILoggerFactory _loggerFactory;

        public DriveService(DotYouContextAccessor contextAccessor, ISystemStorage systemStorage, ILoggerFactory loggerFactory, IMediator mediator,
            IDriveAclAuthorizationService driveAclAuthorizationService, TenantContext tenantContext)
        {
            _contextAccessor = contextAccessor;
            _systemStorage = systemStorage;

            _loggerFactory = loggerFactory;
            _mediator = mediator;
            _driveAclAuthorizationService = driveAclAuthorizationService;
            _tenantContext = tenantContext;
            _longTermStorageManagers = new ConcurrentDictionary<Guid, ILongTermStorageManager>();
            _tempStorageManagers = new ConcurrentDictionary<Guid, ITempStorageManager>();

            InitializeStorageDrives().GetAwaiter().GetResult();
        }

        private readonly object _createDriveLock = new object();
        private readonly byte[] _driveDataType = "drive".ToUtf8ByteArray(); //keep it lower case

        public Task<StorageDrive> CreateDrive(CreateDriveRequest request)
        {
            Guard.Argument(request, nameof(request)).NotNull();
            Guard.Argument(request.Name, nameof(request.Name)).NotNull().NotEmpty();

            if (request.OwnerOnly && request.AllowAnonymousReads)
            {
                throw new YouverseException("A drive cannot be owner-only and allow anonymous reads");
            }

            var mk = _contextAccessor.GetCurrent().Caller.GetMasterKey();

            StorageDrive storageDrive;

            lock (_createDriveLock)
            {
                //driveAlias and type must be unique
                if (null != this.GetDriveIdByAlias(request.TargetDrive, false).GetAwaiter().GetResult())
                {
                    throw new ConfigException("Drive alias and type must be unique");
                }

                var driveKey = new SymmetricKeyEncryptedAes(ref mk);

                var id = Guid.NewGuid();
                var secret = driveKey.DecryptKeyClone(ref mk);

                (byte[] encryptedIdIv, byte[] encryptedIdValue) = AesCbc.Encrypt(id.ToByteArray(), ref secret);

                var sdb = new StorageDriveBase()
                {
                    Id = id,
                    Name = request.Name,
                    TargetDriveInfo = request.TargetDrive,
                    Metadata = request.Metadata,
                    MasterKeyEncryptedStorageKey = driveKey,
                    EncryptedIdIv = encryptedIdIv,
                    EncryptedIdValue = encryptedIdValue,
                    AllowAnonymousReads = request.AllowAnonymousReads,
                    OwnerOnly = request.OwnerOnly
                };

                secret.Wipe();

                _systemStorage.ThreeKeyValueStorage.Upsert(sdb.Id, request.TargetDrive.ToKey(), _driveDataType, sdb);

                storageDrive = ToStorageDrive(sdb);
                storageDrive.EnsureDirectories();
            }

            _mediator.Publish(new DriveDefinitionAddedNotification()
            {
                IsNewDrive = true,
                Drive = storageDrive
            });

            return Task.FromResult(storageDrive);
        }

        public Task SetDriveReadMode(Guid driveId, bool allowAnonymous)
        {
            _contextAccessor.GetCurrent().Caller.AssertHasMasterKey();
            StorageDrive storageDrive = this.GetDrive(driveId).GetAwaiter().GetResult();

            if (storageDrive.TargetDriveInfo == SystemDriveConstants.ContactDrive || storageDrive.TargetDriveInfo == SystemDriveConstants.ProfileDrive)
            {
                throw new YouverseSecurityException("Cannot change system drive");
            }

            if (storageDrive.OwnerOnly && allowAnonymous)
            {
                throw new YouverseSecurityException("Cannot set Owner Only drive to allow anonymous");
            }

            //only change if needed
            if (storageDrive.AllowAnonymousReads != allowAnonymous)
            {
                storageDrive.AllowAnonymousReads = allowAnonymous;

                _systemStorage.ThreeKeyValueStorage.Upsert(driveId, storageDrive.TargetDriveInfo.ToKey(), _driveDataType, storageDrive);

                _mediator.Publish(new DriveDefinitionAddedNotification()
                {
                    IsNewDrive = false,
                    Drive = storageDrive
                });
            }

            return Task.CompletedTask;
        }


        public async Task<StorageDrive> GetDrive(Guid driveId, bool failIfInvalid = false)
        {
            var sdb = _systemStorage.ThreeKeyValueStorage.Get<StorageDriveBase>(driveId);
            if (null == sdb)
            {
                if (failIfInvalid)
                {
                    throw new InvalidDriveException(driveId);
                }

                return null;
            }

            var drive = ToStorageDrive(sdb);
            return drive;
        }

        public async Task<Guid?> GetDriveIdByAlias(TargetDrive targetDrive, bool failIfInvalid = false)
        {
            var list = _systemStorage.ThreeKeyValueStorage.GetByKey2<StorageDriveBase>(targetDrive.ToKey());
            var drives = list as StorageDriveBase[] ?? list.ToArray();
            if (!drives.Any())
            {
                if (failIfInvalid)
                {
                    throw new InvalidDriveException(targetDrive.Alias);
                }

                return null;
            }

            var drive = ToStorageDrive(drives.Single());
            return drive.Id;
        }

        public async Task<PagedResult<StorageDrive>> GetDrives(PageOptions pageOptions)
        {
            return await this.GetDrivesInternal(true, pageOptions);
        }

        public async Task<PagedResult<StorageDrive>> GetDrives(GuidId type, PageOptions pageOptions)
        {
            Func<StorageDrive, bool> predicate = drive => drive.TargetDriveInfo.Type == type;

            if (_contextAccessor.GetCurrent().Caller.IsAnonymous)
            {
                predicate = drive => drive.TargetDriveInfo.Type == type && drive.AllowAnonymousReads == true && drive.OwnerOnly == false;
            }

            var page = await this.GetDrivesInternal(false, pageOptions);

            var storageDrives = page.Results.Where(predicate).ToList();
            var results = new PagedResult<StorageDrive>(pageOptions, 1, storageDrives);
            return results;
        }

        public async Task<PagedResult<StorageDrive>> GetAnonymousDrives(PageOptions pageOptions)
        {
            var page = await this.GetDrivesInternal(false, pageOptions);
            // var storageDrives = page.Results.Where(drive => drive.AllowAnonymousReads && drive.OwnerOnly == false).ToList();
            var storageDrives = page.Results.Where(drive => drive.AllowAnonymousReads).ToList();
            var results = new PagedResult<StorageDrive>(pageOptions, 1, storageDrives);
            return results;
        }

        public InternalDriveFileId CreateInternalFileId(Guid driveId)
        {
            //TODO: need a permission specifically for writing to the temp drive
            //_contextAccessor.GetCurrent().PermissionsContext.AssertCanWriteToDrive(driveId);

            var df = new InternalDriveFileId()
            {
                FileId = GetLongTermStorageManager(driveId).CreateFileId(),
                DriveId = driveId,
            };

            return df;
        }

        public async Task UpdateFileHeader(InternalDriveFileId file, ServerFileHeader header)
        {
            Guard.Argument(header, nameof(header)).NotNull();
            Guard.Argument(header, nameof(header)).Require(x => x.IsValid());

            _contextAccessor.GetCurrent().PermissionsContext.AssertCanWriteToDrive(file.DriveId);

            var metadata = header.FileMetadata;

            //TODO: need to encrypt the metadata parts
            metadata.File = file; //TBH it's strange having this but we need the metadata to have the file and drive embedded

            if (this.FileExists(file))
            {
                var existingHeader = await this.GetServerFileHeader(file);
                metadata.Updated = UnixTimeUtcMilliseconds.Now().milliseconds;
                metadata.Created = existingHeader.FileMetadata.Created;
            }
            else
            {
                metadata.Created = UnixTimeUtcMilliseconds.Now().milliseconds;
            }

            await WriteFileHeaderInternal(header);
        }

        public Task WritePartStream(InternalDriveFileId file, FilePart filePart, Stream stream)
        {
            _contextAccessor.GetCurrent().PermissionsContext.AssertCanWriteToDrive(file.DriveId);

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

        private void AssertCanReadDrive(Guid driveId)
        {
            var drive = this.GetDrive(driveId, true).GetAwaiter().GetResult();
            if (!drive.AllowAnonymousReads)
            {
                _contextAccessor.GetCurrent().PermissionsContext.AssertCanReadDrive(driveId);
            }
        }

        public Task WriteTempStream(InternalDriveFileId file, string extension, Stream stream)
        {
            //TODO: need a permission specifically for writing to the t4mep drive
            //_contextAccessor.GetCurrent().PermissionsContext.AssertCanWriteToDrive(file.DriveId);

            return GetTempStorageManager(file.DriveId).WriteStream(file.FileId, extension, stream);
        }

        public Task<Stream> GetTempStream(InternalDriveFileId file, string extension)
        {
            this.AssertCanReadDrive(file.DriveId);

            return GetTempStorageManager(file.DriveId).GetStream(file.FileId, extension);
        }

        public Task DeleteTempFile(InternalDriveFileId file, string extension)
        {
            _contextAccessor.GetCurrent().PermissionsContext.AssertCanWriteToDrive(file.DriveId);

            return GetTempStorageManager(file.DriveId).Delete(file.FileId, extension);
        }

        public Task DeleteTempFiles(InternalDriveFileId file)
        {
            _contextAccessor.GetCurrent().PermissionsContext.AssertCanWriteToDrive(file.DriveId);

            return GetTempStorageManager(file.DriveId).Delete(file.FileId);
        }

        public Task<IEnumerable<ServerFileHeader>> GetMetadataFiles(Guid driveId, PageOptions pageOptions)
        {
            _contextAccessor.GetCurrent().PermissionsContext.AssertCanReadDrive(driveId);

            return GetLongTermStorageManager(driveId).GetServerFileHeaders(pageOptions);
        }

        public async Task<Stream> GetThumbnailPayloadStream(InternalDriveFileId file, int width, int height)
        {
            this.AssertCanReadDrive(file.DriveId);

            //Note: calling to get the file header so we can ensure the caller can read this file
            var _ = await this.GetServerFileHeader(file);

            var stream = await GetLongTermStorageManager(file.DriveId).GetThumbnail(file.FileId, width, height);
            return stream;
        }

        public async Task WriteThumbnailStream(InternalDriveFileId file, int width, int height, Stream stream)
        {
            _contextAccessor.GetCurrent().PermissionsContext.AssertCanWriteToDrive(file.DriveId);
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

            var size = await GetLongTermStorageManager(file.DriveId).GetPayloadFileSize(file.FileId);
            header.FileMetadata.PayloadSize = size;

            return header;
        }

        public async Task<Stream> GetPayloadStream(InternalDriveFileId file)
        {
            this.AssertCanReadDrive(file.DriveId);

            //Note: calling to get the file header so we can ensure the caller can read this file

            var _ = await this.GetServerFileHeader(file);
            var stream = await GetLongTermStorageManager(file.DriveId).GetFilePartStream(file.FileId, FilePart.Payload);
            return stream;
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
            _contextAccessor.GetCurrent().PermissionsContext.AssertCanWriteToDrive(file.DriveId);

            var existingHeader = await this.GetServerFileHeader(file);

            var deletedServerFileHeader = new ServerFileHeader()
            {
                EncryptedKeyHeader = existingHeader.EncryptedKeyHeader,
                FileMetadata = new FileMetadata(existingHeader.FileMetadata.File)
                {
                    FileState = FileState.Deleted,
                    Updated = UnixTimeUtcMilliseconds.Now().milliseconds,
                    GlobalTransitId = existingHeader.FileMetadata.GlobalTransitId
                },
                ServerMetadata = existingHeader.ServerMetadata
            };

            await this.WriteFileHeaderInternal(deletedServerFileHeader);
            await GetLongTermStorageManager(file.DriveId).SoftDelete(file.FileId);
        }

        public Task HardDeleteLongTermFile(InternalDriveFileId file)
        {
            _contextAccessor.GetCurrent().PermissionsContext.AssertCanWriteToDrive(file.DriveId);

            var result = GetLongTermStorageManager(file.DriveId).HardDelete(file.FileId);

            var notification = new DriveFileDeletedNotification()
            {
                File = file
            };

            _mediator.Publish(notification);

            return result;
        }

        public async Task CommitTempFileToLongTerm(InternalDriveFileId file, KeyHeader keyHeader, FileMetadata metadata, ServerMetadata serverMetadata, string payloadExtension)
        {
            _contextAccessor.GetCurrent().PermissionsContext.AssertCanWriteToDrive(file.DriveId);

            //TODO: this method is so hacky 🤢
            metadata.File = file;

            var storageManager = GetLongTermStorageManager(file.DriveId);
            var tempStorageManager = GetTempStorageManager(file.DriveId);

            string sourceFile = await tempStorageManager.GetPath(file.FileId, payloadExtension);
            await storageManager.MoveToLongTerm(file.FileId, sourceFile, FilePart.Payload);

            if (metadata.AppData.AdditionalThumbnails != null)
                foreach (var thumb in metadata.AppData.AdditionalThumbnails)
                {
                    var extension = this.GetThumbnailFileExtension(thumb.PixelWidth, thumb.PixelHeight);
                    var sourceThumbnail = await tempStorageManager.GetPath(file.FileId, extension);
                    await storageManager.MoveThumbnailToLongTerm(file.FileId, sourceThumbnail, thumb.PixelWidth, thumb.PixelHeight);
                }

            //TODO: calculate payload checksum, put on file metadata
            var serverHeader = new ServerFileHeader()
            {
                EncryptedKeyHeader = await this.EncryptKeyHeader(file.DriveId, keyHeader),
                FileMetadata = metadata,
                ServerMetadata = serverMetadata
            };

            //Note: calling write metadata last since it will call OnLongTermFileChanged to ensure it is indexed
            await this.UpdateFileHeader(file, serverHeader);
        }

        private async Task<PagedResult<StorageDrive>> GetDrivesInternal(bool enforceSecurity, PageOptions pageOptions)
        {
            var storageDrives = _systemStorage.ThreeKeyValueStorage.GetByKey3<StorageDriveBase>(_driveDataType);

            if (_contextAccessor.GetCurrent()?.Caller?.IsOwner ?? false)
            {
                return new PagedResult<StorageDrive>(pageOptions, 1, storageDrives.Select(ToStorageDrive).ToList());
            }

            Func<StorageDriveBase, bool> predicate = null;
            predicate = drive => drive.OwnerOnly == false;
            if (enforceSecurity)
            {
                if (_contextAccessor.GetCurrent().Caller.IsAnonymous)
                {
                    predicate = drive => drive.AllowAnonymousReads == true && drive.OwnerOnly == false;
                }
            }

            return new PagedResult<StorageDrive>(pageOptions, 1, storageDrives.Where(predicate).Select(ToStorageDrive).ToList());
        }

        private void OnLongTermFileChanged(InternalDriveFileId file, ServerFileHeader header)
        {
            var notification = new DriveFileChangedNotification()
            {
                File = file,
                FileHeader = header
            };

            _mediator.Publish(notification);
        }

        private StorageDrive ToStorageDrive(StorageDriveBase sdb)
        {
            //TODO: this should probably go in config
            const string driveFolder = "drives";
            return new StorageDrive(
                Path.Combine(_tenantContext.StorageConfig.DataStoragePath, driveFolder),
                Path.Combine(_tenantContext.StorageConfig.TempStoragePath, driveFolder), sdb);
        }

        private async Task InitializeStorageDrives()
        {
            var drives = _systemStorage.ThreeKeyValueStorage.GetByKey3<StorageDriveBase>(_driveDataType) ?? new List<StorageDrive>();
            foreach (var drive in drives.Select(ToStorageDrive))
            {
                LoadLongTermStorage(drive, out var _);
            }
        }

        private ILongTermStorageManager GetLongTermStorageManager(Guid driveId)
        {
            if (_longTermStorageManagers.TryGetValue(driveId, out var manager))
            {
                return manager;
            }

            var sd = this.GetDrive(driveId, failIfInvalid: true).GetAwaiter().GetResult();
            var success = LoadLongTermStorage(sd, out manager);
            if (!success)
            {
                throw new StorageException($"Could not load long term storage for drive {driveId}");
            }

            return manager;
        }

        private ITempStorageManager GetTempStorageManager(Guid driveId)
        {
            if (_tempStorageManagers.TryGetValue(driveId, out var manager))
            {
                return manager;
            }

            var sd = this.GetDrive(driveId, failIfInvalid: true).GetAwaiter().GetResult();
            var success = LoadTempStorage(sd, out manager);
            if (!success)
            {
                throw new StorageException($"Could not load temporary storage for drive {driveId}");
            }

            return manager;
        }

        private bool LoadLongTermStorage(StorageDrive drive, out ILongTermStorageManager manager)
        {
            var logger = _loggerFactory.CreateLogger<ILongTermStorageManager>();
            manager = new FileBasedLongTermStorageManager(drive, logger);
            return _longTermStorageManagers.TryAdd(drive.Id, manager);
        }

        private bool LoadTempStorage(StorageDrive drive, out ITempStorageManager manager)
        {
            var logger = _loggerFactory.CreateLogger<ITempStorageManager>();
            manager = new FileBasedTempStorageManager(drive, logger);
            return _tempStorageManagers.TryAdd(drive.Id, manager);
        }

        private async Task WriteFileHeaderInternal(ServerFileHeader header)
        {
            var json = DotYouSystemSerializer.Serialize(header);
            var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
            await GetLongTermStorageManager(header.FileMetadata.File.DriveId).WritePartStream(header.FileMetadata.File.FileId, FilePart.Header, stream);
            OnLongTermFileChanged(header.FileMetadata.File, header);
        }
    }
}