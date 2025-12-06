using System;
using System.Threading.Tasks;
using Odin.Hosting.Controllers.Base.Drive.Status;
using Odin.Hosting.UnifiedV2;
using Refit;

namespace Odin.Hosting.Tests._V2.ApiClient;

public interface IDriveStatusHttpClientApiV2
{
    private const string RootDriveEndpoint = UnifiedApiRouteConstants.ByDriveId;

    [Get(RootDriveEndpoint + "/status")]
    Task<ApiResponse<DriveStatus>> GetDriveStatus([AliasAs("driveId:guid")] Guid driveId);
}