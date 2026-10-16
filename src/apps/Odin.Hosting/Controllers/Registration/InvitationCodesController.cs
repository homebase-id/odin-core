using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Odin.Services.Registry.Registration;

namespace Odin.Hosting.Controllers.Registration;
#nullable enable

public record CreateInvitationCodeRequest(string? Owner, int MaxUses);

/// <summary>
/// Creates and lists invitation codes (#1908), on the provisioning domain, for odin-admin and chat-kmp.
/// The store validates the owner and the number of uses (400).
/// </summary>
[ApiController]
[Route("/api/registration/v1/invitation-codes")]
[ServiceFilter(typeof(RegistrationRestrictedAttribute))]
[ServiceFilter(typeof(InvitationCodeKeyRestrictedAttribute))]
public class InvitationCodesController(InvitationCodeStore invitationCodes) : ControllerBase
{
    [HttpPost]
    public async Task<InvitationCodeInfo> Create([FromBody] CreateInvitationCodeRequest request)
    {
        return await invitationCodes.CreateAsync(request.Owner, request.MaxUses);
    }

    //

    [HttpGet]
    public async Task<List<InvitationCodeInfo>> List([FromQuery] string? owner)
    {
        return await invitationCodes.ListByOwnerAsync(owner);
    }
}
