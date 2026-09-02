using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Odin.Core.Identity;
using Odin.Hosting.Controllers.Base;
using Odin.Hosting.Controllers.Base.Transit;
using Odin.Hosting.UnifiedV2.Authentication.Policy;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.Drives.FileSystem.Base.Upload;
using Odin.Services.Drives.Management;
using Odin.Services.Peer.Outgoing.Drive;
using Odin.Services.Peer.Outgoing.Drive.Query;
using Odin.Services.Peer.Outgoing.Drive.Transfer;
using Odin.Services.Util;
using Swashbuckle.AspNetCore.Annotations;

namespace Odin.Hosting.UnifiedV2.Drive.Write;

/// <summary>
/// Writes to a drive hosted by another identity, named by slug:
/// <c>POST /api/v2/peer/{odinId}/apps/{appSlug}/drives/{driveSlug}/files/send</c> — the case
/// docs/drive-addressing.md leads with, since sending to another identity's chat drive otherwise
/// requires both hosts to share hardcoded guid constants.
/// </summary>
/// <remarks>
/// The guid twin (<see cref="V2DrivePeerWriteController"/>) ignores its route entirely: the remote
/// drive and the recipients both come from the multipart <c>instructions</c> part, and the
/// <c>{driveId}</c> segment is decoration.  Here the path is the address, so the resolved drive and
/// the single recipient named by <c>{odinId}</c> are stamped over whatever the body carried.  A body
/// that disagrees is overridden rather than rejected — the URL is the more specific statement of
/// intent, and the alternative is failing a request whose meaning is unambiguous.
///
/// Resolution happens once per request against the recipient, and is cached by
/// <see cref="PeerDriveQueryService.ResolveRemoteDriveAsync"/>.
/// </remarks>
[ApiController]
[Route(UnifiedApiRouteConstants.PeerAppDriveBySlug)]
[UnifiedV2Authorize(UnifiedPolicies.OwnerOrApp)]
[ApiExplorerSettings(GroupName = "v2")]
[NoSharedSecretOnRequest]
[NoSharedSecretOnResponse]
public class V2PeerAppDriveWriteController(
    ILogger<V2PeerAppDriveWriteController> logger,
    PeerOutgoingTransferService peerOutgoingTransferService,
    PeerDriveQueryService peerDriveQueryService,
    DriveManager driveManager,
    FileSystemResolver fileSystemResolver)
    : PeerSenderControllerBase(logger, peerOutgoingTransferService, driveManager, fileSystemResolver)
{
    /// <summary>
    /// Sends a file to a drive on another identity, named by slug.  The file is not stored on your
    /// own drive.
    /// </summary>
    /// <remarks>
    /// This is the case slug addressing exists for: sending to another identity's drive without both
    /// sides sharing hardcoded guid constants.
    ///
    /// <para><b>Example</b> —
    /// <c>POST /api/v2/peer/frodo.dotyou.cloud/apps/chat/drives/messages/files/send</c></para>
    ///
    /// <para>Multipart body, identical to the guid route: an <c>instructions</c> part
    /// (<c>TransitInstructionSet</c>) followed by metadata and payload parts.  <b>Two fields of the
    /// instructions part are ignored here</b> — <c>remoteTargetDrive</c> and <c>recipients</c> — because
    /// the URL already states both.  Send them or omit them; the path wins either way.</para>
    ///
    /// <para>You need a Write grant on whatever the address resolves to, and either
    /// <c>UseTransitWrite</c> or <c>UseTransitRead</c>.  A deposit-only grant is enough: you do not
    /// need to be able to read the drive you are writing to.</para>
    ///
    /// <para>400 when nothing on that identity answers to the address.</para>
    /// </remarks>
    [HttpPost("files/send")]
    [SwaggerOperation(Tags = [SwaggerInfo.NewStuff])]
    public override Task<TransitResult> SendFile()
    {
        return base.SendFile();
    }

    /// <summary>
    /// Asks another identity to delete a file you previously sent to a drive named by slug.
    /// </summary>
    /// <remarks>
    /// Addressed by GlobalTransitId, which is the same on both sides — your copy and theirs have
    /// different FileIds, so that is the only handle that works here.
    ///
    /// <para><b>Example</b> —
    /// <c>POST /api/v2/peer/frodo.dotyou.cloud/apps/chat/drives/messages/files/senddeleterequest</c></para>
    ///
    /// <para>As with send, the body's <c>targetDrive</c> and <c>recipients</c> are ignored — the URL
    /// states both.  Supply <c>globalTransitIdFileIdentifier.globalTransitId</c> and
    /// <c>fileSystemType</c>.</para>
    ///
    /// <para>400 when nothing on that identity answers to the address.</para>
    /// </remarks>
    /// <param name="request">The file to delete.  Its <c>targetDrive</c> and <c>recipients</c> are
    /// overwritten from the route.</param>
    [HttpPost("files/senddeleterequest")]
    [SwaggerOperation(Tags = [SwaggerInfo.NewStuff])]
    public override async Task<IActionResult> DeleteFile([FromBody] DeleteFileByGlobalTransitIdRequest request)
    {
        OdinValidationUtils.AssertNotNull(request, nameof(request));
        OdinValidationUtils.AssertNotNull(request.GlobalTransitIdFileIdentifier,
            nameof(request.GlobalTransitIdFileIdentifier));

        var (recipient, targetDrive) = await ResolveFromRouteAsync();

        request.GlobalTransitIdFileIdentifier.TargetDrive = targetDrive;
        request.Recipients = [recipient.DomainName];

        return await base.DeleteFile(request);
    }

    protected override async Task<UploadInstructionSet> RemapTransitInstructionSet(Stream transitInstructionStream)
    {
        var instructionSet = await base.RemapTransitInstructionSet(transitInstructionStream);

        var (recipient, targetDrive) = await ResolveFromRouteAsync();

        instructionSet.TransitOptions.RemoteTargetDrive = targetDrive;
        instructionSet.TransitOptions.Recipients = [recipient.DomainName];

        return instructionSet;
    }

    /// <summary>
    /// Reads the address out of the route.  The inherited actions take no parameters — their
    /// signatures belong to the guid form — so the segments are read here rather than bound.
    /// </summary>
    private async Task<(OdinId Recipient, TargetDrive TargetDrive)> ResolveFromRouteAsync()
    {
        var odinId = RouteData.Values["odinId"]?.ToString();
        var appSlug = RouteData.Values["appSlug"]?.ToString();
        var driveSlug = RouteData.Values["driveSlug"]?.ToString();

        AssertIsValidOdinId(odinId, out var id);

        var targetDrive = await peerDriveQueryService.ResolveRemoteDriveAsync(id, appSlug, driveSlug, WebOdinContext);
        return (id, targetDrive);
    }
}
