using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Odin.Core;
using Odin.Hosting.Controllers.Base;
using Odin.Hosting.Controllers.ClientToken.Shared.Drive;
using Odin.Hosting.UnifiedV2.Authentication.Policy;
using Odin.Services.Base;
using Odin.Services.Base.SharedTypes;
using Odin.Services.Peer.Outgoing.Drive.Query;
using Swashbuckle.AspNetCore.Annotations;

namespace Odin.Hosting.UnifiedV2.Drive.Read;

/// <summary>
/// Another identity's drives of one type, as it lets this identity see them: the V2 twin of V1's
/// <c>transit/query/metadata/type</c>.
/// </summary>
[ApiController]
[Route(UnifiedApiRouteConstants.PeerDrivesRoot)]
[UnifiedV2Authorize(UnifiedPolicies.OwnerOrApp)]
[ApiExplorerSettings(GroupName = "v2")]
public class V2PeerDrivesByTypeController(PeerDriveQueryService peerDriveQueryService) : OdinControllerBase
{
    [HttpGet]
    [SwaggerOperation(Tags = [SwaggerInfo.DriveMetadata])]
    public async Task<PagedResult<ClientDriveData>> GetDrivesByType([FromRoute] string odinId, [FromQuery] Guid type)
    {
        AssertIsValidOdinId(odinId, out var id);

        var drives = await peerDriveQueryService.GetDrivesByTypeAsync(id, type,
            GetHttpFileSystemResolver().GetFileSystemType(), WebOdinContext);

        var results = drives.Select(drive => new ClientDriveData
        {
            TargetDrive = drive.TargetDrive,
            Attributes = drive.Attributes
        }).ToList();

        return new PagedResult<ClientDriveData>(PageOptions.All, 1, results);
    }
}

/// <summary>
/// What another identity grants this identity: the V2 twin of V1's <c>transit/query/security/context</c>, and the peer
/// counterpart of <c>/auth/context</c>.
/// </summary>
[ApiController]
[Route(UnifiedApiRouteConstants.PeerAuth)]
[UnifiedV2Authorize(UnifiedPolicies.OwnerOrApp)]
[ApiExplorerSettings(GroupName = "v2")]
public class V2PeerSecurityContextController(PeerDriveQueryService peerDriveQueryService) : OdinControllerBase
{
    [HttpGet("context")]
    [SwaggerOperation(Tags = [SwaggerInfo.Auth])]
    public async Task<RedactedOdinContext> GetSecurityContext([FromRoute] string odinId)
    {
        AssertIsValidOdinId(odinId, out var id);
        return await peerDriveQueryService.GetRemoteDotYouContextAsync(id, WebOdinContext);
    }
}
