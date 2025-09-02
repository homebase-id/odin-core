using System.Net.Http;
using System.Threading.Tasks;
using Odin.Core;
using Odin.Services.Authentication.Owner;
using Odin.Services.Drives.Management;
using Odin.Hosting.Controllers.OwnerToken;
using Odin.Hosting.Controllers.OwnerToken.Drive;
using Refit;

namespace Odin.Hosting.Tests.OwnerApi.Drive.Management
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

        [Post(RootEndpoint + "/updatemetadata")]
        Task<ApiResponse<bool>> UpdateMetadata([Body] UpdateDriveDefinitionRequest request);
        
        [Post(RootEndpoint + "/UpdateAttributes")]
        Task<ApiResponse<bool>> UpdateAttributes([Body] UpdateDriveDefinitionRequest request);
        
        [Post(RootEndpoint + "/setdrivereadmode")]
        Task<ApiResponse<HttpContent>> SetDriveReadMode([Body] UpdateDriveReadModeRequest request);

        [Post(RootEndpoint + "/set-allow-subscriptions")]
        Task<ApiResponse<HttpContent>> SetAllowSubscriptions([Body] UpdateDriveAllowSubscriptionsRequest request);

        [Post(RootEndpoint + "/set-archive-drive")]
        Task<ApiResponse<HttpContent>> SetArchiveDriveFlag([Body] UpdateDriveArchiveFlag request);
    }
}