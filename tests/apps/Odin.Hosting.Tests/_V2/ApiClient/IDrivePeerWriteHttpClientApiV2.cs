using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Odin.Hosting.Controllers.Base.Transit;
using Odin.Hosting.UnifiedV2;
using Odin.Services.Peer.Outgoing.Drive;
using Odin.Services.Peer.Outgoing.Drive.Transfer;
using Refit;

namespace Odin.Hosting.Tests._V2.ApiClient;

public interface IDrivePeerWriteHttpClientApiV2
{
    // Direct write-over-peer: posts a multipart TransitInstructionSet/metadata/payload bundle that
    // the server stages on the transient temp drive and sends to the RemoteTargetDrive owner.
    [Multipart]
    [Post(UnifiedApiRouteConstants.PeerByDriveId + "/files/send")]
    Task<ApiResponse<TransitResult>> SendFile(
        [AliasAs("odinId")] string odinId,
        [AliasAs("driveId:guid")] Guid driveId,
        StreamPart[] streamdata);

    // Delete-over-peer: asks the remote owner to delete a file by its global transit id.
    [Post(UnifiedApiRouteConstants.PeerByDriveId + "/files/senddeleterequest")]
    Task<ApiResponse<Dictionary<string, DeleteLinkedFileStatus>>> SendDeleteRequest(
        [AliasAs("odinId")] string odinId,
        [AliasAs("driveId:guid")] Guid driveId,
        [Body] DeleteFileByGlobalTransitIdRequest request);
}
