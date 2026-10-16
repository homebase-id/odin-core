using Odin.Core.Time;

namespace Odin.Services.Onboarding;

#nullable enable

/// <summary>
/// What signup knew about how an identity came to be (#1908). Signup hands it over on the registration;
/// first run copies it into the identity's own database, which is the copy that counts from then on.
/// Read-only to callers: only <see cref="HandledAt"/> changes, once.
/// </summary>
public record OnboardingInfo
{
    /// <summary>
    /// The owner of the invitation code the identity signed up with; null for a configured code or none
    /// </summary>
    public string? InvitedBy { get; init; }

    /// <summary>
    /// The invitation code the identity signed up with, if any
    /// </summary>
    public string? InvitationCode { get; init; }

    public UnixTimeUtc? SignedUpAt { get; init; }

    /// <summary>
    /// When the Chat app acted on this; null until then. Set once for the whole identity.
    /// </summary>
    public UnixTimeUtc? HandledAt { get; init; }
}
