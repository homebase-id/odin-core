using System.Collections.Generic;
using System.Threading.Tasks;
using Odin.Hosting.Controllers.ClientToken.Shared.Drive;
using Odin.Hosting.UnifiedV2;
using Refit;

namespace Odin.Hosting.Tests._V2.ApiClient;

/// <summary>
/// The local slug-addressed drive routes: <c>/api/v2/apps/{appSlug}/drives[/{driveSlug}]</c>.
/// </summary>
public interface IAppDriveHttpClientApiV2
{
    [Get(UnifiedApiRouteConstants.AppDrivesRoot)]
    Task<ApiResponse<List<ClientDriveData>>> GetAppDrives([AliasAs("appSlug")] string appSlug,
        [Query] string type = null);

    [Get(UnifiedApiRouteConstants.AppDriveBySlug)]
    Task<ApiResponse<ClientDriveData>> GetAppDrive([AliasAs("appSlug")] string appSlug,
        [AliasAs("driveSlug")] string driveSlug);
}
