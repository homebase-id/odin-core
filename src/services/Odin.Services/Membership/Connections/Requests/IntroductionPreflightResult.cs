using System.Collections.Generic;

namespace Odin.Services.Membership.Connections.Requests;

public enum IntroductionPreflightStatus
{
    Ready = 1,

    /// <summary>
    /// We do not have a valid ICR with the recipient, so a peer call cannot be made.
    /// </summary>
    NotConnected = 2,

    /// <summary>
    /// The recipient identity server has not completed initial setup.
    /// </summary>
    RecipientNotConfigured = 3,

    /// <summary>
    /// The recipient identity server requires a version upgrade.
    /// </summary>
    RecipientRequiresUpgrade = 4,

    /// <summary>
    /// The recipient has not granted us the AllowIntroductions permission, so an
    /// introduction would be rejected at the recipient.
    /// </summary>
    IntroductionsNotPermitted = 5,

    /// <summary>
    /// The recipient explicitly denied the preflight (Forbidden or equivalent).
    /// </summary>
    RecipientRejected = 6,

    /// <summary>
    /// The recipient could not be reached (transport / DNS / socket failure or timeout).
    /// </summary>
    Unreachable = 7,

    /// <summary>
    /// An unexpected error occurred. See <see cref="RecipientPreflightStatus.Detail"/>.
    /// </summary>
    UnknownError = 99,
}

public class RecipientPreflightStatus
{
    public string Recipient { get; set; }
    public IntroductionPreflightStatus Status { get; set; }

    /// <summary>
    /// Optional diagnostic string; populated for non-Ready outcomes.
    /// </summary>
    public string Detail { get; set; }

    public bool IsConfigured { get; set; }
    public bool RequiresUpgrade { get; set; }
    public bool AllowsIntroductions { get; set; }
}

public class IntroductionPreflightResult
{
    public List<RecipientPreflightStatus> Recipients { get; set; } = new();
}

/// <summary>
/// Wire response from the recipient's preflight peer endpoint.
/// </summary>
public class PeerIntroductionPreflightResponse
{
    public bool IsConfigured { get; set; }
    public bool RequiresUpgrade { get; set; }
    public bool AllowsIntroductions { get; set; }
}
