using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Drive.Query.LiteDb;
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
            var id = Guid.NewGuid();
            var sdb = new StorageDriveBase()
            {
                Id = id,
                Name = name,
            };

            _systemStorage.WithTenantSystemStorage<StorageDriveBase>(DriveCollectionName, s => s.Save(sdb));

            return Task.FromResult(ToStorageDrive(sdb));
        }

        public event EventHandler<DriveFileChangedArgs> FileMetaDataChanged;

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
            //TODO: use time based guid
            var df = new DriveFileId()
            {
                FileId = Guid.NewGuid(),
                DriveId = driveId,
            };

            return df;
        }

        public Task WriteMetaData(DriveFileId file, FileMetaData data, StorageDisposition storageDisposition = StorageDisposition.LongTerm)
        {
            var json = JsonConvert.SerializeObject(data);
            var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
            var task = GetStorageManager(file.DriveId).WritePartStream(file.FileId, FilePart.Metadata, stream, storageDisposition);
            OnFileMetadataChanged(file);
            return task;
        }

        public Task WritePartStream(DriveFileId file, FilePart filePart, Stream stream, StorageDisposition storageDisposition = StorageDisposition.LongTerm)
        {
            if (filePart == FilePart.Metadata)
            {
                
            }
            var task = GetStorageManager(file.DriveId).WritePartStream(file.FileId, filePart, stream, storageDisposition);
            OnFileMetadataChanged(file);
            return task;
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

        public Task<EncryptedKeyHeader> GetKeyHeader(DriveFileId file, StorageDisposition storageDisposition = StorageDisposition.LongTerm)
        {
            return GetStorageManager(file.DriveId).GetKeyHeader(file.FileId, storageDisposition);
        }

        public void AssertFileIsValid(DriveFileId file, StorageDisposition storageDisposition = StorageDisposition.LongTerm)
        {
            GetStorageManager(file.DriveId).AssertFileIsValid(file.FileId, storageDisposition);
        }

        public Task Delete(DriveFileId file, StorageDisposition storageDisposition = StorageDisposition.LongTerm)
        {
            return GetStorageManager(file.DriveId).Delete(file.FileId, storageDisposition);
        }

        public Task MoveToLongTerm(DriveFileId file)
        {
            return GetStorageManager(file.DriveId).MoveToLongTerm(file.FileId);
        }

        public Task MoveToTemp(DriveFileId file)
        {
            return GetStorageManager(file.DriveId).MoveToTemp(file.FileId);
        }

        public Task WriteKeyHeader(DriveFileId file, EncryptedKeyHeader encryptedKeyHeader, StorageDisposition storageDisposition = StorageDisposition.LongTerm)
        {
            return GetStorageManager(file.DriveId).WriteKeyHeader(file.FileId, encryptedKeyHeader, storageDisposition);
        }

        public StorageDriveIndex GetCurrentIndex(Guid driveId)
        {
            return GetStorageManager(driveId).GetCurrentIndex();
        }

        public Task RebuildAllIndices()
        {
            //TODO: optimize by making this parallel processed or something
            foreach (var sm in _storageManagers.Values)
            {
                sm.RebuildIndex();
            }

            return Task.CompletedTask;
        }

        public Task RebuildIndex(Guid driveId)
        {
            return GetStorageManager(driveId).RebuildIndex();
        }

        private void OnFileMetadataChanged(DriveFileId file)
        {
            EventHandler<DriveFileChangedArgs> handler = this.FileMetaDataChanged;
            if (null != handler)
            {
                handler(this, new DriveFileChangedArgs() {File = file});
            }
        }

        private StorageDrive ToStorageDrive(StorageDriveBase sdb)
        {
            return new StorageDrive(_context.StorageConfig.DataStoragePath, _context.StorageConfig.TempStoragePath, sdb);
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
            manager.LoadLatestIndex().GetAwaiter().GetResult();
            return _storageManagers.TryAdd(drive.Id, manager);
        }
    }
}