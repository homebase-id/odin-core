using System;
using System.Net.Http;
using System.Threading.Tasks;
using Refit;
using Youverse.Core;
using Youverse.Core.Services.Drive;
using Youverse.Hosting.Controllers.OwnerToken;
using Youverse.Hosting.Controllers.OwnerToken.Drive;

namespace Youverse.Hosting.Tests.OwnerApi.Drive
{
    /// <summary>
    /// The interface for storing files
    /// </summary>
    public interface IDriveManagementHttpClient
    {
        private const string RootEndpoint = OwnerApiPathConstants.DrivesV1;
        private const string RootPath = "/api/owner/v1/drive/index";

        [Post(RootEndpoint + "/create")]
        Task<ApiResponse<HttpContent>> CreateDrive(TargetDrive targetDrive, string name, string metadata, bool allowAnonymousReads);

        [Get(RootEndpoint)]
        Task<ApiResponse<PagedResult<OwnerClientDriveData>>> GetDrives(int pageNumber, int pageSize);
        
        [Post(RootPath + "/rebuildallindices")]
        Task<ApiResponse<bool>> RebuildAll();

        [Post(RootPath + "/rebuildindex")]
        Task<ApiResponse<bool>> Rebuild(Guid driveId);
    }
}