using System;
using System.Threading.Tasks;
using Odin.Hosting.UnifiedV2;
using Odin.Services.Peer.Outgoing.Drive.Query;
using Refit;

namespace Odin.Hosting.Tests._V2.ApiClient;

public interface IDrivePeerQueryHttpClientApiV2
{
    private const string Endpoint = UnifiedApiRouteConstants.PeerFilesRoot;

    [Post(Endpoint + "/file-exists")]
    Task<ApiResponse<bool>> GetFileExists([AliasAs("driveId:guid")] Guid driveId,
        [Body] PeerFileExistsByUidAndVersionTagRequest request);
}
