using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
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

        private const string DriveStatusCollection = "drive_stat";

        public DriveManager(DotYouContext context, ISystemStorage systemStorage)
        {
            _context = context;
            _systemStorage = systemStorage;
        }

        public Task<StorageDrive> GetDrive(Guid driveId)
        {
            var driveRootPath = Path.Combine(_context.StorageConfig.DataStoragePath, driveId.ToString("N"));

            var drive = new StorageDrive()
            {
                Id = driveId,
                RootPath = driveRootPath,
            };

            return Task.FromResult(drive);
        }
        
        public Task<PagedResult<StorageDrive>> GetDrives(PageOptions pageOptions)
        {
            //TODO:looks these up somewhere
            var page = new PagedResult<StorageDrive>()
            {
                Request = pageOptions,
                Results = new List<StorageDrive>()
                {
                    new StorageDrive()
                    {
                        Id = DataAttributeDriveId
                    }
                },
                TotalPages = 1
            };

            return Task.FromResult(page);
        }
    }
}