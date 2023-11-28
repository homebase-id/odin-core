﻿using System.Net.Http;
using System.Threading.Tasks;
using Odin.Core;
using Odin.Core.Services.Authentication.Owner;
using Odin.Core.Services.Drives.Management;
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
    }
}