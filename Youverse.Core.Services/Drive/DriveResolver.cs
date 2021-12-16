using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Youverse.Core.Services.Drive
{
    public class DriveResolver : IDriveResolver
    {
        //HACK: total hack.  define the data attributes as a fixed drive until we move them to use the actual storage 
        public static readonly Guid DataAttributeDriveId = Guid.Parse("11111234-2931-4fa1-0000-CCCC40000001");

        public Task<DriveInfo> Resolve(Guid driveId)
        {
            var container = new DriveInfo()
            {
                DriveId = driveId,
                IndexName = $"d_{driveId:N}_idx"  //format is based on what litedb supports for collection names as that's what we used at inception
            };

            return Task.FromResult(container);
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
                        DriveId = DataAttributeDriveId
                    }
                },
                TotalPages = 1
            };

            return Task.FromResult(page);
        }
    }
}