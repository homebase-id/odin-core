using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Odin.Core.Util;
using Odin.Services.Registry.Registration;

namespace Odin.Hosting.Controllers.Registration;
#nullable enable

public record CreateInvitationCodeRequest(string? Owner, int MaxUses);

/// <summary>
/// Creates and lists invitation codes (#1908), on the provisioning domain, for odin-admin and chat-kmp.
/// PROTOTYPE: guarded by the hard-coded key in <see cref="InvitationCodePrototype"/>.
/// </summary>
[ApiController]
[Route("/api/registration/v1/invitation-codes")]
[ServiceFilter(typeof(RegistrationRestrictedAttribute))]
public class InvitationCodesController(InvitationCodeStore invitationCodes) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<InvitationCodeInfo>> Create([FromBody] CreateInvitationCodeRequest request)
    {
        var refused = Refuse();
        if (refused != null)
        {
            return refused;
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
        var refused = Refuse();
        if (refused != null)
        {
            return refused;
        }
        if (!IsValidOwner(owner))
        {
            return BadRequest("Invalid owner");
        }

        return await invitationCodes.ListByOwnerAsync(owner!);
    }

    //

    private ActionResult? Refuse()
    {
        if (!invitationCodes.IsEnabled)
        {
            return NotFound();
        }

        var presented = Encoding.UTF8.GetBytes(Request.Headers[InvitationCodePrototype.ApiKeyHeader].ToString());
        var expected = Encoding.UTF8.GetBytes(InvitationCodePrototype.ApiKey);
        return CryptographicOperations.FixedTimeEquals(presented, expected) ? null : Unauthorized();
    }

    private static bool IsValidOwner(string? owner)
    {
        return owner != null && AsciiDomainNameValidator.TryValidateDomain(owner.Trim().ToLowerInvariant());
    }
}
