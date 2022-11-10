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
        private const string RootEndpoint = OwnerApiPathConstants.DriveManagementV1;

        [Post(RootEndpoint + "/create")]
        Task<ApiResponse<HttpContent>> CreateDrive([Body] CreateDriveRequest request);

        [Post(RootEndpoint)]
        Task<ApiResponse<PagedResult<OwnerClientDriveData>>> GetDrives([Body] GetDrivesRequest request);

        [Post(RootEndpoint + "/setdrivemode")]
        Task<ApiResponse<bool>> SetDriveReadMode([Body] UpdateDriveDefinitionRequest request);
        
        [Post(RootEndpoint + "/updatemetadata")]
        Task<ApiResponse<bool>> UpdateMetadata([Body] UpdateDriveDefinitionRequest request);
    }
}