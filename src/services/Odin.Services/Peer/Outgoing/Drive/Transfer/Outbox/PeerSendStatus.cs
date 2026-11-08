using Odin.Core.Time;

namespace Odin.Services.Peer.Outgoing.Drive.Transfer.Outbox;

/// <summary>
/// Whether a send from the transient drive is still waiting in its sender's outbox
/// (<see cref="PeerOutbox.GetTransientSendStatusAsync"/>).  Once not pending, its result came as a
/// <see cref="Files.PeerSendResultNotification"/>, and the recipient's copy shows whether it landed.
/// </summary>
public class PeerSendStatus
{
    /// <summary>Still in the outbox: not yet sent, being sent, or to be tried again</summary>
    public bool Pending { get; init; }

    /// <summary>Being sent right now: its result comes once the recipient answers</summary>
    public bool Sending { get; init; }

    /// <summary>
    /// How many times the outbox has taken it up to send; a wait the recipient asked for (paused, out of storage) adds
    /// none, and a send cut off by a restart adds one
    /// </summary>
    public int Attempts { get; init; }

    /// <summary>When the outbox next takes it up; null while being sent, or when not pending</summary>
    public UnixTimeUtc? NextAttempt { get; init; }
}
