using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Drive.Storage;
using Youverse.Core.Services.Transit.Encryption;

namespace Youverse.Core.Services.Drive
{
    public class StorageService : IStorageService
    {
        private readonly ISystemStorage _systemStorage;
        private readonly DotYouContext _context;

        private const string DriveCollectionName = "drives";

        public StorageService(DotYouContext context, ISystemStorage systemStorage)
        {
            _context = context;
            _systemStorage = systemStorage;
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

        public Guid CreateFileId(Guid driveId)
        {
            throw new NotImplementedException();
        }

        public Task WritePartStream(Guid driveId, Guid fileId, FilePart filePart, Stream stream, StorageDisposition storageDisposition = StorageDisposition.LongTerm)
        {
            throw new NotImplementedException();
        }

        public Task<long> GetFileSize(Guid driveId, Guid fileId, StorageDisposition storageDisposition = StorageDisposition.LongTerm)
        {
            throw new NotImplementedException();
        }

        public Task<Stream> GetFilePartStream(Guid driveId, Guid fileId, FilePart filePart, StorageDisposition storageDisposition = StorageDisposition.LongTerm)
        {
            throw new NotImplementedException();
        }

        public Task<StorageDisposition> GetStorageType(Guid driveId, Guid fileId)
        {
            throw new NotImplementedException();
        }

        public Task<EncryptedKeyHeader> GetKeyHeader(Guid driveId, Guid fileId, StorageDisposition storageDisposition = StorageDisposition.LongTerm)
        {
            throw new NotImplementedException();
        }

        public void AssertFileIsValid(Guid driveId, Guid fileId, StorageDisposition storageDisposition = StorageDisposition.LongTerm)
        {
            throw new NotImplementedException();
        }

        public Task Delete(Guid driveId, Guid fileId, StorageDisposition storageDisposition = StorageDisposition.LongTerm)
        {
            throw new NotImplementedException();
        }

        public Task MoveToLongTerm(Guid driveId, Guid fileId)
        {
            throw new NotImplementedException();
        }

        public Task MoveToTemp(Guid driveId, Guid fileId)
        {
            throw new NotImplementedException();
        }

        public Task WriteKeyHeader(Guid driveId, Guid fileId, EncryptedKeyHeader encryptedKeyHeader, StorageDisposition storageDisposition = StorageDisposition.LongTerm)
        {
            throw new NotImplementedException();
        }

        private StorageDrive ToStorageDrive(StorageDriveBase sdb)
        {
            return new StorageDrive(_context.StorageConfig.DataStoragePath, _context.StorageConfig.TempStoragePath, sdb);
        }
    }
}