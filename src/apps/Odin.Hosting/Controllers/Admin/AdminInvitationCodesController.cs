using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Odin.Core.Util;
using Odin.Services.Registry.Registration;

namespace Odin.Hosting.Controllers.Admin;
#nullable enable

public record CreateInvitationCodeRequest(string? Owner, int MaxUses);

/// <summary>
/// Invitation codes owned by identities (#1908), for odin-admin. 400 when this host has them off.
/// </summary>
[ApiController]
[Route(AdminApiPathConstants.BasePathV1 + "/invitation-codes")]
[ServiceFilter(typeof(AdminApiRestrictedAttribute))]
[ApiExplorerSettings(GroupName = "admin-v1")]
public class AdminInvitationCodesController(InvitationCodeStore invitationCodes) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<InvitationCodeInfo>> Create([FromBody] CreateInvitationCodeRequest request)
    {
        if (!invitationCodes.IsEnabled)
        {
            return BadRequest("Invitation codes are not enabled on this host");
        }
        if (!IsValidOwner(request.Owner))
        {
            return BadRequest("Invalid owner");
        }
        if (request.MaxUses is < 1 or > InvitationCodeStore.MaxUsesLimit)
        {
            return BadRequest($"MaxUses must be between 1 and {InvitationCodeStore.MaxUsesLimit}");
        }

        return await invitationCodes.CreateAsync(request.Owner!, request.MaxUses);
    }

    //

    [HttpGet]
    public async Task<ActionResult<List<InvitationCodeInfo>>> List([FromQuery] string? owner)
    {
        if (!invitationCodes.IsEnabled)
        {
            return BadRequest("Invitation codes are not enabled on this host");
        }
        if (!IsValidOwner(owner))
        {
            return BadRequest("Invalid owner");
        }

        return await invitationCodes.ListByOwnerAsync(owner!);
    }

    //

    private static bool IsValidOwner(string? owner)
    {
        return owner != null && AsciiDomainNameValidator.TryValidateDomain(owner.Trim().ToLowerInvariant());
    }
}
