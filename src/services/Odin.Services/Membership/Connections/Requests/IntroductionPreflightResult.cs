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
    /// The recipient identity server requires a version upgrade which is not yet running.
    /// </summary>
    RecipientRequiresUpgrade = 4,

    /// <summary>
    /// The recipient is connected to us and has confirmed the connection, but has deliberately not
    /// granted AllowIntroductions. This is the only status that describes a decision by the recipient.
    /// </summary>
    IntroductionsNotPermitted = 5,

    /// <summary>
    /// The recipient explicitly denied the preflight (Forbidden or equivalent).
    /// </summary>
    RecipientRejected = 6,

    /// <summary>
    /// The recipient could not be reached and the transport failure could not be classified further.
    /// </summary>
    Unreachable = 7,

    /// <summary>
    /// The recipient is connected to us, but only as an auto-connection (introduction- or app-originated)
    /// that their owner has not confirmed. Nothing is broken and nothing was denied: confirming requires
    /// the recipient owner's master key, so the connection sits in the Auto-connected circle -- which
    /// carries no AllowIntroductions -- until they act.
    /// </summary>
    RecipientConnectionNotConfirmed = 8,

    /// <summary>
    /// The recipient has no usable connection record for us, so our ICR is one-sided. Deliberately also
    /// covers the case where the recipient has blocked us: reporting that separately would disclose the
    /// block to its target.
    /// </summary>
    RecipientDoesNotRecognizeConnection = 9,

    /// <summary>
    /// The recipient has a connection record for us but its peer key store is invalid, so their server
    /// could not build a connected context for the call. Repairable.
    /// </summary>
    RecipientConnectionNeedsRepair = 10,

    /// <summary>
    /// Our own ICR with the recipient exists but could not produce a usable client access token. The
    /// fault is on our side.
    /// </summary>
    SenderConnectionInvalid = 11,

    /// <summary>
    /// The recipient runs a build that predates the preflight endpoint, so nothing can be determined
    /// about whether an introduction would succeed.
    /// </summary>
    PreflightNotSupported = 12,

    /// <summary>
    /// The recipient is currently running a version upgrade. Transient; distinct from
    /// <see cref="RecipientRequiresUpgrade"/>, which means an upgrade is needed and is not running.
    /// </summary>
    RecipientUpgradeInProgress = 13,

    /// <summary>
    /// The recipient's domain could not be resolved.
    /// </summary>
    RecipientUnresolvable = 14,

    /// <summary>
    /// The TLS handshake with the recipient failed (expired/invalid certificate or similar).
    /// </summary>
    RecipientCertificateInvalid = 15,

    /// <summary>
    /// The recipient did not respond within the timeout.
    /// </summary>
    RecipientTimedOut = 16,

    /// <summary>
    /// The recipient's host resolved but refused the connection.
    /// </summary>
    RecipientConnectionRefused = 17,

    /// <summary>
    /// An unexpected error occurred. See <see cref="RecipientPreflightStatus.Detail"/>.
    /// </summary>
    UnknownError = 99,
}

/// <summary>
/// What the recipient's server was able to determine about the calling identity's connection to it.
/// Reported alongside <see cref="PeerIntroductionPreflightResponse.AllowsIntroductions"/> so the caller
/// can tell "you were never confirmed" and "I don't know you" apart from "I decided no".
/// </summary>
public enum PeerCallerConnectionState
{
    /// <summary>
    /// The recipient did not report a connection state -- either it runs a build that predates this
    /// field, or it could not determine one. Callers must not infer anything from this value.
    /// </summary>
    Unknown = 0,

    /// <summary>
    /// The request resolved a valid, connected ICR on the recipient's side.
    /// </summary>
    Connected = 1,

    /// <summary>
    /// The recipient has no usable connection record for the caller. Deliberately merges "no ICR at all",
    /// "ICR not in a connected state", and "caller is blocked" -- see the privacy note on
    /// <see cref="IntroductionPreflightStatus.RecipientDoesNotRecognizeConnection"/>.
    /// </summary>
    NotRecognized = 2,

    /// <summary>
    /// A connection record exists but its peer key store is invalid, so a connected context could not be
    /// built for this request. Repairable, and may self-heal on the next key store upgrade.
    /// </summary>
    NeedsRepair = 3,
}

/// <summary>
/// Who, if anyone, is able to act on a given <see cref="IntroductionPreflightStatus"/>. Lets a client
/// route statuses generically instead of hard-coding a branch per status.
/// </summary>
public enum PreflightRemedyActor
{
    /// <summary>No one can act; the condition either clears on its own or cannot be determined.</summary>
    None = 0,

    /// <summary>The calling identity (or its owner) can act -- e.g. repair or re-establish the connection.</summary>
    Caller = 1,

    /// <summary>The recipient must act -- e.g. confirm the connection or finish setting up their identity.</summary>
    Recipient = 2,
}

public class RecipientPreflightStatus
{
    public string Recipient { get; set; }
    public IntroductionPreflightStatus Status { get; set; }

    /// <summary>
    /// Diagnostic string for logs and support; populated for non-Ready outcomes. It may contain raw
    /// transport or exception text and is <b>not</b> user-facing copy -- clients must render text based
    /// on <see cref="Status"/>, never on this field.
    /// </summary>
    public string Detail { get; set; }

    public bool IsConfigured { get; set; }
    public bool RequiresUpgrade { get; set; }

    /// <summary>
    /// Whether the recipient granted AllowIntroductions to us. A <c>false</c> here is not on its own a
    /// statement about the recipient's intent -- read <see cref="Status"/> instead.
    /// </summary>
    public bool AllowsIntroductions { get; set; }

    /// <summary>
    /// Whether the recipient resolved a connected context for our call (as opposed to falling back to
    /// its anonymous/authenticated context).
    /// </summary>
    public bool IsCallerConnected { get; set; }

    /// <summary>
    /// Whether the recipient has us in its Confirmed Connections circle.
    /// </summary>
    public bool IsCallerConfirmed { get; set; }

    /// <summary>
    /// Whether the recipient has us in its Auto-connected circle. Read with <see cref="IsCallerConfirmed"/>
    /// this separates "never confirmed" (auto-connected, awaiting the recipient owner) from "confirmed and
    /// then revoked" (in neither system circle) -- both of which leave AllowIntroductions false.
    /// </summary>
    public bool IsCallerAutoConnected { get; set; }

    /// <summary>
    /// The recipient's view of our connection to it. <see cref="PeerCallerConnectionState.Unknown"/>
    /// when the recipient did not report one.
    /// </summary>
    public PeerCallerConnectionState CallerConnectionState { get; set; }

    /// <summary>
    /// Who can act on <see cref="Status"/>. Derived; not sent by the recipient.
    /// </summary>
    public PreflightRemedyActor RemedyActor => IntroductionPreflightStatusInfo.GetRemedyActor(Status);

    /// <summary>
    /// Whether <see cref="Status"/> is expected to clear on its own, making a retry worthwhile.
    /// Derived; not sent by the recipient.
    /// </summary>
    public bool IsTransient => IntroductionPreflightStatusInfo.IsTransient(Status);
}

/// <summary>
/// Classification of <see cref="IntroductionPreflightStatus"/> values so clients can decide how to
/// present a status without hard-coding a branch for each one.
/// </summary>
public static class IntroductionPreflightStatusInfo
{
    public static PreflightRemedyActor GetRemedyActor(IntroductionPreflightStatus status)
    {
        switch (status)
        {
            case IntroductionPreflightStatus.NotConnected:
            case IntroductionPreflightStatus.SenderConnectionInvalid:
            case IntroductionPreflightStatus.RecipientDoesNotRecognizeConnection:
            case IntroductionPreflightStatus.RecipientConnectionNeedsRepair:
                return PreflightRemedyActor.Caller;

            case IntroductionPreflightStatus.RecipientNotConfigured:
            case IntroductionPreflightStatus.RecipientRequiresUpgrade:
            case IntroductionPreflightStatus.IntroductionsNotPermitted:
            case IntroductionPreflightStatus.RecipientRejected:
            case IntroductionPreflightStatus.RecipientConnectionNotConfirmed:
            case IntroductionPreflightStatus.RecipientCertificateInvalid:
            case IntroductionPreflightStatus.RecipientUnresolvable:
                return PreflightRemedyActor.Recipient;

            default:
                return PreflightRemedyActor.None;
        }
    }

    public static bool IsTransient(IntroductionPreflightStatus status)
    {
        switch (status)
        {
            case IntroductionPreflightStatus.Unreachable:
            case IntroductionPreflightStatus.RecipientUpgradeInProgress:
            case IntroductionPreflightStatus.RecipientTimedOut:
            case IntroductionPreflightStatus.RecipientConnectionRefused:
                return true;

            default:
                return false;
        }
    }
}

public class IntroductionPreflightResult
{
    /// <summary>
    /// Per-recipient results. Note that the caller's own identity is filtered out of the request, so
    /// this list can be shorter than the list of recipients that was submitted.
    /// </summary>
    public List<RecipientPreflightStatus> Recipients { get; set; } = new();
}

/// <summary>
/// Wire response from the recipient's preflight peer endpoint.
/// </summary>
public class PeerIntroductionPreflightResponse
{
    public bool IsConfigured { get; set; }
    public bool RequiresUpgrade { get; set; }

    /// <summary>
    /// Whether the caller holds AllowIntroductions in this request's permission context. Only meaningful
    /// together with <see cref="IsCallerConnected"/> and <see cref="IsCallerConfirmed"/>: the permission
    /// is granted solely by the Confirmed Connections circle, so it is also false for an unconfirmed
    /// auto-connection and for a caller we do not recognize at all.
    /// </summary>
    public bool AllowsIntroductions { get; set; }

    /// <summary>
    /// Whether this request resolved a connected context for the caller, rather than falling back to the
    /// authenticated-but-unconnected context.
    /// </summary>
    public bool IsCallerConnected { get; set; }

    /// <summary>
    /// Whether the caller is in our Confirmed Connections circle.
    /// </summary>
    public bool IsCallerConfirmed { get; set; }

    /// <summary>
    /// Whether the caller is in our Auto-connected circle. Together with <see cref="IsCallerConfirmed"/>
    /// this tells an unconfirmed auto-connection apart from a connection that was confirmed and then had
    /// the circle revoked: the former is in Auto-connected, the latter is in neither.
    /// </summary>
    public bool IsCallerAutoConnected { get; set; }

    /// <summary>
    /// Our view of the caller's connection to us. Absent (<see cref="PeerCallerConnectionState.Unknown"/>)
    /// on servers that predate this field.
    /// </summary>
    public PeerCallerConnectionState CallerConnectionState { get; set; }
}
