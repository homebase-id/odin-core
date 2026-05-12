using System;
using System.Threading.Tasks;
using Odin.Hosting.UnifiedV2;
using Odin.Services.Peer.Outgoing.Drive.Query;
using Refit;

namespace Odin.Hosting.Tests._V2.ApiClient;

public interface IDrivePeerQueryHttpClientApiV2
{
    [Get(UnifiedApiRouteConstants.PeerByUniqueId + "/exists")]
    Task<ApiResponse<FileExistsOnPeerResponse>> GetFileExistsByUid(
        [AliasAs("odinId")] string odinId,
        [AliasAs("driveId:guid")] Guid driveId,
        [AliasAs("uid:guid")] Guid uid);

    [Get(UnifiedApiRouteConstants.PeerByGtid + "/exists")]
    Task<ApiResponse<FileExistsOnPeerResponse>> GetFileExistsByGtid(
        [AliasAs("odinId")] string odinId,
        [AliasAs("driveId:guid")] Guid driveId,
        [AliasAs("gtid:guid")] Guid gtid);
}
