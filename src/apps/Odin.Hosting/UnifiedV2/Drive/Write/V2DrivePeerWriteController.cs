using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Odin.Hosting.Controllers.Base.Transit;
using Odin.Hosting.UnifiedV2.Authentication.Policy;
using Odin.Services.Base;
using Odin.Services.Drives.Management;
using Odin.Services.Peer.Outgoing.Drive.Transfer;

namespace Odin.Hosting.UnifiedV2.Drive.Write
{
    /// <summary>
    /// Writes a file directly to a drive hosted by another (peer) identity, without keeping a local
    /// copy — the V2 "over peer" equivalent of <c>POST /api/apps/v1/transit/sender/files/send</c>.
    /// Reuses the V1 multipart upload + transient-temp-drive transfer path verbatim
    /// (<see cref="PeerSenderControllerBase"/>): the multipart <c>instructions</c> part carries the
    /// <c>RemoteTargetDrive</c> and <c>Recipients</c>, so the file is staged transiently and sent to
    /// the owner over the outbox rather than stored on the caller's drive.
    ///
    /// Inherits <c>POST files/send</c> and <c>POST files/senddeleterequest</c>, producing
    /// <c>/api/v2/peer/{odinId}/drives/{driveId}/files/send</c> (and .../senddeleterequest).
    /// </summary>
    [ApiController]
    [Route(UnifiedApiRouteConstants.PeerByDriveId)]
    [UnifiedV2Authorize(UnifiedPolicies.OwnerOrApp)]
    [ApiExplorerSettings(GroupName = "v2")]
    [NoSharedSecretOnRequest]
    [NoSharedSecretOnResponse]
    public class V2DrivePeerWriteController(
        ILogger<V2DrivePeerWriteController> logger,
        PeerOutgoingTransferService peerOutgoingTransferService,
        DriveManager driveManager,
        FileSystemResolver fileSystemResolver)
        : PeerSenderControllerBase(logger, peerOutgoingTransferService, driveManager, fileSystemResolver);
}
