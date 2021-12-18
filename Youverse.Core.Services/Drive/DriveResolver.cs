using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Youverse.Core.Services.Base;

namespace Youverse.Core.Services.Drive
{
    public class DriveResolver : IDriveResolver
    {
        //HACK: total hack.  define the data attributes as a fixed drive until we move them to use the actual storage 
        public static readonly Guid DataAttributeDriveId = Guid.Parse("11111234-2931-4fa1-0000-CCCC40000001");

        private readonly DotYouContext _context;

        public DriveResolver(DotYouContext context)
        {
            _context = context;
        }

        public Task<DriveInfo> Resolve(Guid driveId)
        {
            var driveRootPath = Path.Combine(_context.StorageConfig.DataStoragePath, driveId.ToString("N"));
            var indexRootPath = Path.Combine(driveRootPath, "_idx");
            var indexName = $"d_idx";
            var permissionIndexName = $"d_prm_idx";
            
            var drive = new DriveInfo()
            {
                Id = driveId,
                RootPath = driveRootPath,
                IndexName = indexName,  //format is based on what litedb supports for collection names as that's what we used at inception
                PermissionIndexName = permissionIndexName,
                IndexPath = Path.Combine(indexRootPath, indexName),
                PermissionIndexPath = Path.Combine(indexRootPath, permissionIndexName)
            };

            return Task.FromResult(drive);
        }

        public Task<PagedResult<DriveInfo>> GetDrives(PageOptions pageOptions)
        {
            //TODO:looks these up somewhere
            var page = new PagedResult<DriveInfo>()
            {
                Request = pageOptions,
                Results = new List<DriveInfo>()
                {
                    new DriveInfo()
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