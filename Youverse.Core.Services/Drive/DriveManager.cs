using System;
using System.Linq;
using System.Threading.Tasks;
using Youverse.Core.Services.Base;

namespace Youverse.Core.Services.Drive
{
    public class DriveManager : IDriveManager
    {
        //HACK: total hack.  define the data attributes as a fixed drive until we move them to use the actual storage 
        public static readonly Guid DataAttributeDriveId = Guid.Parse("11111234-2931-4fa1-0000-CCCC40000001");
        private readonly ISystemStorage _systemStorage;
        private readonly DotYouContext _context;

        private const string DriveCollectionName = "drives";

        public DriveManager(DotYouContext context, ISystemStorage systemStorage)
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
                if(failIfInvalid)
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
            var pDrive = ToStorageDrive(new StorageDriveBase()
            {
                Id = DataAttributeDriveId,
                Name = "ProfileStoreHack"
            });

            var page = await _systemStorage.WithTenantSystemStorageReturnList<StorageDriveBase>(DriveCollectionName, s => s.GetList(pageOptions));
            page.Results.Add(pDrive);

            var storageDrives = page.Results.Select(ToStorageDrive).ToList();
            var converted = new PagedResult<StorageDrive>(pageOptions, page.TotalPages, storageDrives);
            return converted;
        }

        private StorageDrive ToStorageDrive(StorageDriveBase sdb)
        {
            return new StorageDrive(_context.StorageConfig.DataStoragePath, sdb);
        }
    }
}