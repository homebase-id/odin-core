using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Odin.Services.Base;
using Odin.Services.Configuration;

namespace Odin.Services.Registry.Registration;

#nullable enable

public record OwnerInvitationCode(string Code, int MaxUses, int UsesLeft, string Link);

/// <summary>
/// The owner's own invitation codes, for the owner console and the owner's apps (#1908).
/// </summary>
public class OwnerInvitationCodeService(OdinConfiguration configuration, InvitationCodeStore invitationCodes)
{
    /// <summary>
    /// The calling owner's codes, each with the signup link to share. Always the tenant's own: the owner
    /// is never taken from the caller, so no one can list another identity's codes. An identity that has
    /// none yet (it signed up before codes existed, or its code was not created at signup) gets one now.
    /// </summary>
    public async Task<List<OwnerInvitationCode>> GetCodesAsync(IOdinContext odinContext)
    {
        odinContext.Caller.AssertCallerIsOwner();

        if (!invitationCodes.IsEnabled)
        {
            return [];
        }

        var owner = odinContext.Tenant.DomainName;
        var codes = await invitationCodes.ListByOwnerAsync(owner);
        if (codes.Count == 0)
        {
            codes = [await invitationCodes.CreateAsync(owner, configuration.Registry.DefaultInvitationCodeUses)];
        }

        return codes.Select(c => new OwnerInvitationCode(c.Code, c.MaxUses, c.UsesLeft, SignupLink(c.Code))).ToList();
    }

    private string SignupLink(string code)
    {
        return $"https://{configuration.Registry.ProvisioningDomain}/?invitation-code={Uri.EscapeDataString(code)}";
    }
}
