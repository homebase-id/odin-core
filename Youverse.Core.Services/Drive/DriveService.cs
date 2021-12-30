using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Dawn;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Drive.Storage;
using Youverse.Core.Services.Transit.Encryption;

namespace Youverse.Core.Services.Drive
{
    public class DriveService : IDriveService
    {
        private readonly ISystemStorage _systemStorage;
        private readonly DotYouContext _context;
        private readonly ConcurrentDictionary<Guid, IStorageManager> _storageManagers;
        private const string DriveCollectionName = "drives";

        private readonly ILoggerFactory _loggerFactory;

        public DriveService(DotYouContext context, ISystemStorage systemStorage, ILoggerFactory loggerFactory)
        {
            _context = context;
            _systemStorage = systemStorage;
            _loggerFactory = loggerFactory;
            _storageManagers = new ConcurrentDictionary<Guid, IStorageManager>();

            InitializeStorageDrives().GetAwaiter().GetResult();
        }

        //TODO: add storage dek here
        public Task<StorageDrive> CreateDrive(string name)
        {
            Guard.Argument(name, nameof(name)).NotNull().NotEmpty();

            var id = Guid.NewGuid();
            var sdb = new StorageDriveBase()
            {
                Id = id,
                Name = name,
            };

            _systemStorage.WithTenantSystemStorage<StorageDriveBase>(DriveCollectionName, s => s.Save(sdb));

            var sd = ToStorageDrive(sdb);
            sd.EnsureDirectories();
            return Task.FromResult(sd);
        }

        public event EventHandler<DriveFileChangedArgs> FileChanged;

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
                FileId = GetStorageManager(driveId).CreateFileId(),
                DriveId = driveId,
            };
            
            return df;
        }

        public Task WriteMetaData(DriveFileId file, FileMetaData metadata, StorageDisposition storageDisposition = StorageDisposition.LongTerm)
        {
            metadata.File = file; //TBH it's strange having this but we need the metadata to have the file and drive embeded
            
            var json = JsonConvert.SerializeObject(metadata);
            var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
            var task = GetStorageManager(file.DriveId).WritePartStream(file.FileId, FilePart.Metadata, stream, storageDisposition);

            if (storageDisposition == StorageDisposition.LongTerm)
            {
                OnLongTermFileChanged(file, metadata);
            }

            return task;
        }

        public async Task WritePayload(DriveFileId file, Stream stream, StorageDisposition storageDisposition = StorageDisposition.LongTerm)
        {
            await GetStorageManager(file.DriveId).WritePartStream(file.FileId, FilePart.Payload, stream, storageDisposition);

            //update the metadata file - updated date
            var metadata = await GetMetadata(file,storageDisposition);
            metadata.Updated = DateTimeExtensions.UnixTimeMilliseconds();
            
            //TODO: who sets the checksum?
            //metadata.FileChecksum
            await this.WriteMetaData(file, metadata, storageDisposition);
        }

        public Task WritePartStream(DriveFileId file, FilePart filePart, Stream stream, StorageDisposition storageDisposition = StorageDisposition.LongTerm)
        {
            var task = GetStorageManager(file.DriveId).WritePartStream(file.FileId, filePart, stream, storageDisposition);
            return task;
        }

        public Task<IEnumerable<FileMetaData>> GetMetadataFiles(Guid driveId, PageOptions pageOptions)
        {
            return GetStorageManager(driveId).GetMetadataFiles(pageOptions);
        }
        
        public Task<EncryptedKeyHeader> GetKeyHeader(DriveFileId file, StorageDisposition storageDisposition = StorageDisposition.LongTerm)
        {
            return GetStorageManager(file.DriveId).GetKeyHeader(file.FileId, storageDisposition);
        }

        public async Task<FileMetaData> GetMetadata(DriveFileId file, StorageDisposition storageDisposition = StorageDisposition.LongTerm)
        {
            var metadata = await GetStorageManager(file.DriveId).GetMetadata(file.FileId, storageDisposition);
            return metadata;
        }

        public async Task<Stream> GetPayloadStream(DriveFileId file, StorageDisposition storageDisposition = StorageDisposition.LongTerm)
        {
            var stream = await GetStorageManager(file.DriveId).GetFilePartStream(file.FileId, FilePart.Payload, storageDisposition);
            return stream;
        }
        
        public Task<long> GetFileSize(DriveFileId file, StorageDisposition storageDisposition = StorageDisposition.LongTerm)
        {
            return GetStorageManager(file.DriveId).GetFileSize(file.FileId, storageDisposition);
        }

        public Task<Stream> GetFilePartStream(DriveFileId file, FilePart filePart, StorageDisposition storageDisposition = StorageDisposition.LongTerm)
        {
            return GetStorageManager(file.DriveId).GetFilePartStream(file.FileId, filePart, storageDisposition);
        }

        public Task<StorageDisposition> GetStorageType(DriveFileId file)
        {
            return GetStorageManager(file.DriveId).GetStorageType(file.FileId);
        }
        
        public void AssertFileIsValid(DriveFileId file, StorageDisposition storageDisposition = StorageDisposition.LongTerm)
        {
            GetStorageManager(file.DriveId).AssertFileIsValid(file.FileId, storageDisposition);
        }

        public Task Delete(DriveFileId file, StorageDisposition storageDisposition = StorageDisposition.LongTerm)
        {
            return GetStorageManager(file.DriveId).Delete(file.FileId, storageDisposition);
        }

        public async Task MoveToLongTerm(DriveFileId file)
        {
            await GetStorageManager(file.DriveId).MoveToLongTerm(file.FileId);

            //HACK: I don't like having to call getmetadata when i move a file.  i wonder if there's a better way
            var metadata = await this.GetMetadata(file, StorageDisposition.LongTerm);
            OnLongTermFileChanged(file, metadata);
        }

        public Task MoveToTemp(DriveFileId file)
        {
            return GetStorageManager(file.DriveId).MoveToTemp(file.FileId);
        }

        public Task WriteKeyHeader(DriveFileId file, EncryptedKeyHeader encryptedKeyHeader, StorageDisposition storageDisposition = StorageDisposition.LongTerm)
        {
            return GetStorageManager(file.DriveId).WriteKeyHeader(file.FileId, encryptedKeyHeader, storageDisposition);
        }
        

        private void OnLongTermFileChanged(DriveFileId file, FileMetaData metaData)
        {
            EventHandler<DriveFileChangedArgs> handler = this.FileChanged;
            if (null != handler)
            {
                handler(this, new DriveFileChangedArgs()
                {
                    File = file,
                    FileMetaData = metaData
                });
            }
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
                Load(drive, out var _);
            }
        }

        private IStorageManager GetStorageManager(Guid driveId)
        {
            if (_storageManagers.TryGetValue(driveId, out var manager))
            {
                return manager;
            }

            var sd = this.GetDrive(driveId, failIfInvalid: true).GetAwaiter().GetResult();
            var success = Load(sd, out manager);
            if (!success)
            {
                throw new StorageException(driveId);
            }

            return manager;
        }

        private bool Load(StorageDrive drive, out IStorageManager manager)
        {
            var logger = _loggerFactory.CreateLogger<IStorageManager>();
            manager = new FileBasedStorageManager(drive, logger);
            return _storageManagers.TryAdd(drive.Id, manager);
        }
    }
}