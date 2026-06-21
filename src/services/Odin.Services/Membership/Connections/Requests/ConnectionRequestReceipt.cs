namespace Odin.Services.Membership.Connections.Requests;

/// <summary>
/// Returned by the recipient in the <c>DeliverConnectionRequest</c> response. Carries the recipient's
/// own public profile card (the JSON served at <c>pub/profile</c>) so the sender can create a named
/// contact on send, in the same round-trip, without a separate keyless fetch. Additive/optional: older
/// recipients return no body, and the sender falls back to the public-profile fetch.
/// </summary>
public class ConnectionRequestReceipt
{
    /// <summary>The recipient's public profile card JSON (<c>{ "name": "..." }</c>), or null/empty.</summary>
    public string RecipientPublicCardJson { get; set; }
}
