using System;
using System.Threading.Tasks;
using Refit;

namespace Youverse.Hosting.Tests.AppAPI.Drive
{
    public interface IOwnerDriveIndexManagementClient
    {
        
        private const string RootPath = "/api/owner/v1/drive/index";
        
        [Post(RootPath + "/rebuildallindices")]
        Task<ApiResponse<bool>> RebuildAll();

        [Post(RootPath + "/rebuildindex")]
        Task<ApiResponse<bool>> Rebuild(Guid driveId);
    }
}