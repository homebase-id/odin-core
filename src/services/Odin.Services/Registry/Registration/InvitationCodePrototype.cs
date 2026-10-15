namespace Odin.Services.Registry.Registration;

/// <summary>
/// PROTOTYPE (#1908) — hard-coded for bleeding edge only. Invitation codes are on by default for this
/// provisioning domain, and codes are created and listed with this key, which odin-admin and chat-kmp
/// also carry. Replace with configured keys before this goes anywhere else.
/// </summary>
public static class InvitationCodePrototype
{
    public const string BleedingEdgeProvisioningDomain = "provisioning.demo.dominion.id";
    public const string ApiKeyHeader = "Odin-Invitation-Code-Key";
    public const string ApiKey = "UiRtjjN0weyVWWym-B_AZzoJUhzkG7tfF5dTw6ZLBYA";
}
