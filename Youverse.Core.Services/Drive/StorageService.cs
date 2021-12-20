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

        public DriveFileId CreateFileId(Guid driveId)
        {
            var df = new DriveFileId()
            {
                FileId = Guid.NewGuid(),
                DriveId = driveId,
            };

            return df;
        }

        public Task WritePartStream(DriveFileId file, FilePart filePart, Stream stream, StorageDisposition storageDisposition = StorageDisposition.LongTerm)
        {
            throw new NotImplementedException();
        }

        public Task<long> GetFileSize(DriveFileId file, StorageDisposition storageDisposition = StorageDisposition.LongTerm)
        {
            throw new NotImplementedException();
        }

        public Task<Stream> GetFilePartStream(DriveFileId file, FilePart filePart, StorageDisposition storageDisposition = StorageDisposition.LongTerm)
        {
            throw new NotImplementedException();
        }

        public Task<StorageDisposition> GetStorageType(DriveFileId file)
        {
            throw new NotImplementedException();
        }

        public Task<EncryptedKeyHeader> GetKeyHeader(DriveFileId file, StorageDisposition storageDisposition = StorageDisposition.LongTerm)
        {
            throw new NotImplementedException();
        }

        public void AssertFileIsValid(DriveFileId file, StorageDisposition storageDisposition = StorageDisposition.LongTerm)
        {
            throw new NotImplementedException();
        }

        public Task Delete(DriveFileId file, StorageDisposition storageDisposition = StorageDisposition.LongTerm)
        {
            throw new NotImplementedException();
        }

        public Task MoveToLongTerm(DriveFileId file)
        {
            throw new NotImplementedException();
        }

        public Task MoveToTemp(DriveFileId file)
        {
            throw new NotImplementedException();
        }

        public Task WriteKeyHeader(DriveFileId file, EncryptedKeyHeader encryptedKeyHeader, StorageDisposition storageDisposition = StorageDisposition.LongTerm)
        {
            throw new NotImplementedException();
        }

        private StorageDrive ToStorageDrive(StorageDriveBase sdb)
        {
            return new StorageDrive(_context.StorageConfig.DataStoragePath, _context.StorageConfig.TempStoragePath, sdb);
        }
    }
}