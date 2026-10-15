using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Odin.Hosting.ApiExceptions.Server;
using Odin.Hosting.Controllers.Base;
using Odin.Hosting.UnifiedV2.Authentication.Policy;
using Odin.Services.Registry.Registration;
using Swashbuckle.AspNetCore.Annotations;

namespace Odin.Hosting.UnifiedV2.InvitationCodes;

public class InvitationCodesResponse
{
    public List<OwnerInvitationCode> Codes { get; init; } = [];
}

/// <summary>
/// The owner's invitation codes to hand out (#1908), for the owner console and the owner's apps (e.g. Chat).
/// </summary>
[ApiController]
[Route(UnifiedApiRouteConstants.InvitationCodes)]
[UnifiedV2Authorize(UnifiedPolicies.OwnerOrApp)]
[ApiExplorerSettings(GroupName = "v2")]
public class V2InvitationCodesController(OwnerInvitationCodeService invitationCodeService) : OdinControllerBase
{
    // GET /api/v2/invitation-codes — the caller's own codes with uses left and the signup link to share;
    // an empty list when this host has no invitation-code service, 503 when it cannot reach it.
    [SwaggerOperation(Tags = [SwaggerInfo.InvitationCodes])]
    [HttpGet]
    [ProducesResponseType(typeof(InvitationCodesResponse), 200)]
    [ProducesResponseType(503)]
    public async Task<InvitationCodesResponse> GetCodes()
    {
        try
        {
            return new InvitationCodesResponse { Codes = await invitationCodeService.GetCodesAsync(WebOdinContext) };
        }
        catch (InvitationCodeServiceException e)
        {
            throw new ServiceUnavailableException("The invitation-code service is unavailable", e);
        }
    }
}
