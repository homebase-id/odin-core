using System;
using System.Net.Http;
using System.Threading.Tasks;
using Refit;
using Youverse.Core;
using Youverse.Core.Services.Drive;
using Youverse.Hosting.Controllers.Apps.Drive;
using Youverse.Hosting.Controllers.Owner;

namespace Youverse.Hosting.Tests.OwnerApi.Drive
{
    /// <summary>
    /// The interface for storing files
    /// </summary>
    public interface IDriveManagementHttpClient
    {
        private const string RootEndpoint = OwnerApiPathConstants.DrivesV1;
        
        [Post(RootEndpoint + "/create")]
        Task<ApiResponse<HttpContent>> CreateDrive(TargetDrive targetDrive, string name, string metadata, bool allowAnonymousReads);

        [Get(RootEndpoint)]
        Task<ApiResponse<PagedResult<ClientDriveData>>> GetDrives(int pageNumber, int pageSize);
    }
}