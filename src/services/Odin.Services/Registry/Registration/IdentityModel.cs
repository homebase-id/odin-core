namespace Odin.Services.Registry.Registration;

public class IdentityModel
{
    public string Email { get; set; }

    // No PlanId: an identity's plan comes only from its invitation code. A planId the client still sends is
    // ignored.

    public string InvitationCode { get; set; }
}