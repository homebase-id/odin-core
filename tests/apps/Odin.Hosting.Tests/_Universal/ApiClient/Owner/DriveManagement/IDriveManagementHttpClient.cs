using System.Net.Http;
using System.Threading.Tasks;
using Odin.Core;
using Odin.Services.Authentication.Owner;
using Odin.Services.Drives.Management;
using Odin.Hosting.Controllers.OwnerToken.Drive;
using Odin.Services.Drives;
using Refit;

namespace Odin.Hosting.Tests._Universal.ApiClient.Owner.DriveManagement
{
    public interface IRefitDriveManagement
    {
        private const string RootEndpoint = OwnerApiPathConstants.DriveManagementV1;

        [Post(RootEndpoint + "/create")]
        Task<ApiResponse<bool>> CreateDrive([Body] CreateDriveRequest request);

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

        [Post(RootEndpoint + "/set-allow-cdn")]
        Task<ApiResponse<HttpContent>> SetAllowCdn([Body] UpdateDriveAllowCdnRequest request);

        [Post(RootEndpoint + "/set-owner")]
        Task<ApiResponse<HttpContent>> SetDriveOwningApp([Body] SetDriveOwningAppRequest request);

        [Post(RootEndpoint + "/reassign-owner")]
        Task<ApiResponse<HttpContent>> ReassignDriveOwningApp([Body] SetDriveOwningAppRequest request);
        
        [Post(RootEndpoint + "/defrag")]
        Task<ApiResponse<HttpContent>> DefragDrive(); // This should be moved to the identity, not on the drive
    }
}