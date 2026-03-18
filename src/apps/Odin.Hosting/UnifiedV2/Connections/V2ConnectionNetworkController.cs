using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Odin.Core;
using Odin.Core.Identity;
using Odin.Hosting.Controllers;
using Odin.Hosting.Controllers.Base;
using Odin.Hosting.Controllers.Base.Membership.Connections;
using Odin.Hosting.UnifiedV2.Authentication.Policy;
using Odin.Services.Membership.Connections;
using Odin.Services.Membership.Connections.Requests;
using Odin.Services.Membership.Connections.Verification;
using Swashbuckle.AspNetCore.Annotations;

namespace Odin.Hosting.UnifiedV2.Connections;

[ApiController]
[Route(UnifiedApiRouteConstants.Connections)]
[UnifiedV2Authorize(UnifiedPolicies.OwnerOrAppOrGuest)]
[ApiExplorerSettings(GroupName = "v2")]
public class V2ConnectionNetworkController(
    CircleNetworkService circleNetwork,
    CircleNetworkVerificationService verificationService
) : OdinControllerBase
{
    [HttpPost("unblock")]
    [SwaggerOperation(Tags = [SwaggerInfo.Connections], Summary = "Unblock a previously blocked identity")]
    public async Task<IActionResult> Unblock([FromBody] OdinIdRequest request)
    {
        await circleNetwork.UnblockAsync((OdinId)request.OdinId, WebOdinContext);
        return Ok();
    }

    [HttpPost("block")]
    [SwaggerOperation(Tags = [SwaggerInfo.Connections], Summary = "Block an identity")]
    public async Task<IActionResult> Block([FromBody] OdinIdRequest request)
    {
        await circleNetwork.BlockAsync((OdinId)request.OdinId, WebOdinContext);
        return Ok();
    }

    [HttpPost("disconnect")]
    [SwaggerOperation(Tags = [SwaggerInfo.Connections], Summary = "Disconnect from an identity")]
    public async Task<IActionResult> Disconnect([FromBody] OdinIdRequest request)
    {
        await circleNetwork.DisconnectAsync((OdinId)request.OdinId, WebOdinContext);
        return Ok();
    }

    [HttpPost("confirm-connection")]
    [SwaggerOperation(Tags = [SwaggerInfo.Connections], Summary = "Confirm a pending connection")]
    public async Task<IActionResult> ConfirmConnection([FromBody] OdinIdRequest request)
    {
        await circleNetwork.ConfirmConnectionAsync((OdinId)request.OdinId, WebOdinContext);
        return Ok();
    }

    [HttpPost("verify-connection")]
    [SwaggerOperation(Tags = [SwaggerInfo.Connections], Summary = "Verify connection status with an identity")]
    public async Task<IcrVerificationResult> VerifyConnection([FromBody] OdinIdRequest request)
    {
        var result = await verificationService.VerifyConnectionAsync(
            (OdinId)request.OdinId,
            HttpContext.RequestAborted,
            WebOdinContext
        );
        return result;
    }

    [HttpPost("troubleshooting-info")]
    [SwaggerOperation(Tags = [SwaggerInfo.Connections], Summary = "Get troubleshooting information for a connection")]
    public async Task<IcrTroubleshootingInfo> GetReconcilableStatus([FromBody] OdinIdRequest request)
    {
        var result = await circleNetwork.GetTroubleshootingInfoAsync((OdinId)request.OdinId, WebOdinContext);
        return result;
    }

    [HttpGet("status")]
    [SwaggerOperation(Tags = [SwaggerInfo.Connections], Summary = "Get connection status for an identity")]
    public async Task<RedactedIdentityConnectionRegistration> GetConnectionInfo(OdinId odinId)
    {
        var result = await circleNetwork.GetIcrAsync(odinId, WebOdinContext);
        return result?.Redacted();
    }

    [HttpGet("connected")]
    [SwaggerOperation(Tags = [SwaggerInfo.Connections], Summary = "Get list of connected identities")]
    public async Task<CursoredResult<RedactedIdentityConnectionRegistration>> GetConnectedIdentities(int count, string cursor)
    {
        var result = await circleNetwork.GetConnectedIdentitiesAsync(count, cursor, WebOdinContext);
        return new CursoredResult<RedactedIdentityConnectionRegistration>()
        {
            Cursor = result.Cursor,
            Results = result.Results.Select(p => p.Redacted()).ToList()
        };
    }

    [HttpGet("blocked")]
    [SwaggerOperation(Tags = [SwaggerInfo.Connections], Summary = "Get list of blocked identities")]
    public async Task<CursoredResult<RedactedIdentityConnectionRegistration>> GetBlockedProfiles(int count, string cursor)
    {
        var result = await circleNetwork.GetBlockedProfilesAsync(count, cursor, WebOdinContext);
        return new CursoredResult<RedactedIdentityConnectionRegistration>()
        {
            Cursor = result.Cursor,
            Results = result.Results.Select(p => p.Redacted()).ToList()
        };
    }

    [HttpGet("circles")]
    [SwaggerOperation(Tags = [SwaggerInfo.Connections], Summary = "Get members of a circle")]
    public async Task<IEnumerable<OdinId>> GetCircleMembers(Guid circleId)
    {
        var result = await circleNetwork.GetCircleMembersAsync(circleId, WebOdinContext);
        return result;
    }

    [HttpPost("circles/add")]
    [SwaggerOperation(Tags = [SwaggerInfo.Connections], Summary = "Add an identity to a circle")]
    public async Task<IActionResult> GrantCircle([FromBody] AddCircleMembershipRequest request)
    {
        await circleNetwork.GrantCircleAsync(request.CircleId, new OdinId(request.OdinId), WebOdinContext);
        return Ok();
    }

    [HttpPost("circles/revoke")]
    [SwaggerOperation(Tags = [SwaggerInfo.Connections], Summary = "Remove an identity from a circle")]
    public async Task<IActionResult> RevokeCircle([FromBody] RevokeCircleMembershipRequest request)
    {
        await circleNetwork.RevokeCircleAccessAsync(request.CircleId, new OdinId(request.OdinId), WebOdinContext);
        return Ok();
    }
}