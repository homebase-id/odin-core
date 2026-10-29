using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using Odin.Hosting.UnifiedV2;
using Odin.Services.Authentication.Owner;
using Refit;

namespace Odin.Hosting.Tests.V2.Api;

/// <summary>
/// Drive requests with untyped bodies, for tests that must send a slug an <c>OdinSlug</c> can't hold
/// (empty, blank or malformed) and check the server refuses it.
/// </summary>
public interface IRawSlugRequests
{
    [Post(OwnerApiPathConstants.DriveManagementV1 + "/create")]
    Task<ApiResponse<HttpContent>> CreateDrive([Body] Dictionary<string, object> request);

    [Post(OwnerApiPathConstants.DriveManagementV1 + "/set-owner")]
    Task<ApiResponse<HttpContent>> SetDriveOwningApp([Body] Dictionary<string, object> request);

    [Post(OwnerApiPathConstants.DriveManagementV1 + "/reassign-owner")]
    Task<ApiResponse<HttpContent>> ReassignDriveOwningApp([Body] Dictionary<string, object> request);

    [Post(OwnerApiPathConstants.AppManagementV1 + "/register/app")]
    Task<ApiResponse<HttpContent>> RegisterApp([Body] Dictionary<string, object> request);

    /// <summary>An app's own circle (app session).</summary>
    [Post(UnifiedApiRouteConstants.Connections + "/circles/create")]
    Task<ApiResponse<HttpContent>> CreateAppCircle([Body] Dictionary<string, object> request);
}
