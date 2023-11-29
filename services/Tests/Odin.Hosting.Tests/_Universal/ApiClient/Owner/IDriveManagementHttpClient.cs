using System.Net.Http;
using System.Threading.Tasks;
using Odin.Core;
using Odin.Core.Services.Authentication.Owner;
using Odin.Core.Services.Drives.Management;
using Odin.Hosting.Controllers.OwnerToken.Drive;
using Refit;

namespace Odin.Hosting.Tests._Universal.ApiClient.Owner
{
    public interface IDriveManagementHttpClient
    {
        private const string RootEndpoint = OwnerApiPathConstants.DriveManagementV1;

        [Post(RootEndpoint + "/create")]
        Task<ApiResponse<bool>> CreateDrive([Body] CreateDriveRequest request);

        [Post(RootEndpoint)]
        Task<ApiResponse<PagedResult<OwnerClientDriveData>>> GetDrives([Body] GetDrivesRequest request);

    }
}