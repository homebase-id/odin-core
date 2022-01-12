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
using Newtonsoft.Json;
using Youverse.Core.Cryptography;
using Youverse.Core.Cryptography.Crypto;
using Youverse.Core.Cryptography.Data;
using Youverse.Core.Exceptions;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Drive.Storage;
using Youverse.Core.Services.Transit.Encryption;

namespace Youverse.Core.Services.Drive
{
    public class DriveService : IDriveService
    {
        private readonly ISystemStorage _systemStorage;
        private readonly IMediator _mediator;
        private readonly DotYouContext _context;
        private readonly ConcurrentDictionary<Guid, ILongTermStorageManager> _longTermStorageManagers;
        private readonly ConcurrentDictionary<Guid, ITempStorageManager> _tempStorageManagers;
        private const string DriveCollectionName = "drives";

        private readonly ILoggerFactory _loggerFactory;

        public DriveService(DotYouContext context, ISystemStorage systemStorage, ILoggerFactory loggerFactory, IMediator mediator)
        {
            _context = context;
            _systemStorage = systemStorage;
            _loggerFactory = loggerFactory;
            _mediator = mediator;
            _longTermStorageManagers = new ConcurrentDictionary<Guid, ILongTermStorageManager>();
            _tempStorageManagers = new ConcurrentDictionary<Guid, ITempStorageManager>();

            InitializeStorageDrives().GetAwaiter().GetResult();
        }
        
        public Task<StorageDrive> CreateDrive(string name)
        {
            Guard.Argument(name, nameof(name)).NotNull().NotEmpty();

            var mk = _context.Caller.GetMasterKey();

            var driveKey = new SymmetricKeyEncryptedAes(mk);
            
            var id = Guid.NewGuid();
            var secret = driveKey.DecryptKey(mk);

            (byte[] encryptedIdIv, byte[] encryptedIdValue) = AesCbc.EncryptBytesToBytes_Aes(id.ToByteArray(), secret);

            var sdb = new StorageDriveBase()
            {
                Id = id,
                Name = name,
                MasterKeyEncryptedStorageKey = driveKey,
                EncryptedIdIv = encryptedIdIv,
                EncryptedIdValue = encryptedIdValue
            };

            secret.Wipe();
            
            _systemStorage.WithTenantSystemStorage<StorageDriveBase>(DriveCollectionName, s => s.Save(sdb));

            var sd = ToStorageDrive(sdb);
            sd.EnsureDirectories();
            return Task.FromResult(sd);
        }

        public async Task<StorageDrive> GetDrive(Guid driveId, bool failIfInvalid = false)
        {
            var sdb = await _systemStorage.WithTenantSystemStorageReturnSingle<StorageDriveBase>(DriveCollectionName, s => s.Get(driveId));
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

        public async Task<PagedResult<StorageDrive>> GetDrives(PageOptions pageOptions)
        {
            var page = await _systemStorage.WithTenantSystemStorageReturnList<StorageDriveBase>(DriveCollectionName, s => s.GetList(pageOptions));
            var storageDrives = page.Results.Select(ToStorageDrive).ToList();
            var converted = new PagedResult<StorageDrive>(pageOptions, page.TotalPages, storageDrives);
            return converted;
        }
        
        public DriveFileId CreateFileId(Guid driveId)
        {
            var df = new DriveFileId()
            {
                FileId = GetLongTermStorageManager(driveId).CreateFileId(),
                DriveId = driveId,
            };
            
            return df;
        }

        public async Task WriteMetaData(DriveFileId file, FileMetadata metadata, StorageDisposition storageDisposition = StorageDisposition.LongTerm)
        {
            Guard.Argument(metadata, nameof(metadata)).NotNull();
            Guard.Argument(metadata.ContentType, nameof(metadata.ContentType)).NotNull().NotEmpty();

            //TODO: need to encrypt the metadata parts
            metadata.File = file; //TBH it's strange having this but we need the metadata to have the file and drive embedded

            if (this.FileExists(file, storageDisposition))
            {
                var existingMetadata = await this.GetMetadata(file, storageDisposition);
                metadata.Updated = DateTimeExtensions.UnixTimeMilliseconds();
                metadata.Created = existingMetadata.Created;
            }
            else
            {
                metadata.Created = DateTimeExtensions.UnixTimeMilliseconds();
            }
            
            var json = JsonConvert.SerializeObject(metadata);
            var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
            var result = GetLongTermStorageManager(file.DriveId).WritePartStream(file.FileId, FilePart.Metadata, stream, storageDisposition);

            if (storageDisposition == StorageDisposition.LongTerm)
            {
                OnLongTermFileChanged(file, metadata);
            }
        }

        public async Task WritePayload(DriveFileId file, Stream stream, StorageDisposition storageDisposition = StorageDisposition.LongTerm)
        {
            await GetLongTermStorageManager(file.DriveId).WritePartStream(file.FileId, FilePart.Payload, stream, storageDisposition);

            //update the metadata file - updated date
            var metadata = await GetMetadata(file,storageDisposition);
            metadata.Updated = DateTimeExtensions.UnixTimeMilliseconds();
            
            //TODO: who sets the checksum?
            //metadata.FileChecksum
            await this.WriteMetaData(file, metadata, storageDisposition);
        }

        public Task WritePartStream(DriveFileId file, FilePart filePart, Stream stream, StorageDisposition storageDisposition = StorageDisposition.LongTerm)
        {
            var task = GetLongTermStorageManager(file.DriveId).WritePartStream(file.FileId, filePart, stream, storageDisposition);
            return task;
        }

        public Task WriteTempStream(DriveFileId file, string extension, Stream stream)
        {
            return GetTempStorageManager(file.DriveId).WriteStream(file.FileId, extension, stream);
        }

        public Task DeleteTempFile(DriveFileId file, string extension)
        {
            return GetTempStorageManager(file.DriveId).Delete(file.FileId, extension);
        }
        
        public Task DeleteTempFiles(DriveFileId file)
        {
            return GetTempStorageManager(file.DriveId).Delete(file.FileId);
        }

        public Task<IEnumerable<FileMetadata>> GetMetadataFiles(Guid driveId, PageOptions pageOptions)
        {
            return GetLongTermStorageManager(driveId).GetMetadataFiles(pageOptions);
        }

        public async Task<EncryptedKeyHeader> WriteKeyHeader(DriveFileId file, KeyHeader keyHeader, StorageDisposition storageDisposition)
        {
            var manager = GetLongTermStorageManager(file.DriveId);
            var drive = manager.Drive;
            var storageKey = _context.AppContext.GetDriveStorageKey(file.DriveId);
            
            //this.AssertKeyMatch(storageKey)
            var decryptedDriveId = AesCbc.DecryptBytesFromBytes_Aes(drive.EncryptedIdValue, storageKey.GetKey(), drive.EncryptedIdIv);
            if (!ByteArrayUtil.EquiByteArrayCompare(decryptedDriveId, drive.Id.ToByteArray()))
            {
                throw new YouverseSecurityException("Invalid key storage attempted to encrypt data");
            }
            
            var encryptedKeyHeader = EncryptedKeyHeader.EncryptKeyHeaderAes(keyHeader, keyHeader.Iv, storageKey.GetKey());
            
            await manager.WriteEncryptedKeyHeader(file.FileId, encryptedKeyHeader, storageDisposition);
            return encryptedKeyHeader;
        }

        public Task<EncryptedKeyHeader> GetEncryptedKeyHeader(DriveFileId file, StorageDisposition storageDisposition = StorageDisposition.LongTerm)
        {
            return GetLongTermStorageManager(file.DriveId).GetKeyHeader(file.FileId, storageDisposition);
        }

        public async Task<FileMetadata> GetMetadata(DriveFileId file, StorageDisposition storageDisposition = StorageDisposition.LongTerm)
        {
            var metadata = await GetLongTermStorageManager(file.DriveId).GetMetadata(file.FileId, storageDisposition);
            return metadata;
        }

        public async Task<Stream> GetPayloadStream(DriveFileId file, StorageDisposition storageDisposition = StorageDisposition.LongTerm)
        {
            var stream = await GetLongTermStorageManager(file.DriveId).GetFilePartStream(file.FileId, FilePart.Payload, storageDisposition);
            return stream;
        }
        
        public Task<long> GetFileSize(DriveFileId file, StorageDisposition storageDisposition = StorageDisposition.LongTerm)
        {
            return GetLongTermStorageManager(file.DriveId).GetFileSize(file.FileId, storageDisposition);
        }

        public Task<Stream> GetFilePartStream(DriveFileId file, FilePart filePart, StorageDisposition storageDisposition = StorageDisposition.LongTerm)
        {
            return GetLongTermStorageManager(file.DriveId).GetFilePartStream(file.FileId, filePart, storageDisposition);
        }

        public Task<StorageDisposition> GetStorageType(DriveFileId file)
        {
            return GetLongTermStorageManager(file.DriveId).GetStorageType(file.FileId);
        }
        
        public void AssertFileIsValid(DriveFileId file, StorageDisposition storageDisposition = StorageDisposition.LongTerm)
        {
            GetLongTermStorageManager(file.DriveId).AssertFileIsValid(file.FileId, storageDisposition);
        }
        
        public bool FileExists(DriveFileId file, StorageDisposition storageDisposition = StorageDisposition.LongTerm)
        {
            return GetLongTermStorageManager(file.DriveId).FileExists(file.FileId, storageDisposition);
        }

        public Task DeleteLongTermFile(DriveFileId file, StorageDisposition storageDisposition = StorageDisposition.LongTerm)
        {
            return GetLongTermStorageManager(file.DriveId).Delete(file.FileId, storageDisposition);
        }

        public async Task MoveToLongTerm(DriveFileId file)
        {
            await GetLongTermStorageManager(file.DriveId).MoveToLongTerm(file.FileId);

            //HACK: I don't like having to call getmetadata when i move a file.  i wonder if there's a better way
            var metadata = await this.GetMetadata(file, StorageDisposition.LongTerm);
            OnLongTermFileChanged(file, metadata);
        }

        public Task MoveToTemp(DriveFileId file)
        {
            return GetLongTermStorageManager(file.DriveId).MoveToTemp(file.FileId);
        }

        public Task WriteEncryptedKeyHeader(DriveFileId file, EncryptedKeyHeader encryptedKeyHeader, StorageDisposition storageDisposition = StorageDisposition.LongTerm)
        {
            return GetLongTermStorageManager(file.DriveId).WriteEncryptedKeyHeader(file.FileId, encryptedKeyHeader, storageDisposition);
        }

        private void OnLongTermFileChanged(DriveFileId file, FileMetadata metadata)
        {
            var notification = new DriveFileChangedNotification()
            {
                File = file,
                FileMetadata = metadata
            };

            _mediator.Publish(notification);
        }

        private StorageDrive ToStorageDrive(StorageDriveBase sdb)
        {
            //TODO: this should probably go in config
            const string driveFolder = "drives";
            return new StorageDrive(
                Path.Combine(_context.StorageConfig.DataStoragePath, driveFolder), 
                Path.Combine(_context.StorageConfig.TempStoragePath, driveFolder), sdb);
        }

        private async Task InitializeStorageDrives()
        {
            var drives = await this.GetDrives(PageOptions.All);
            foreach (var drive in drives.Results)
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
    }
}