using System;
using System.Threading.Tasks;
using Refit;

namespace Youverse.Hosting.Tests.AppAPI.Drive
{
    public interface IOwnerDriveIndexManagementClient
    {
        
        private const string RootPath = "/api/owner/v1/drive/index";
        
        [Post(RootPath + "/rebuildall")]
        Task<ApiResponse<bool>> RebuildAll();

        [Post(RootPath + "/rebuild")]
        Task<ApiResponse<bool>> Rebuild(Guid driveId);
    }
}