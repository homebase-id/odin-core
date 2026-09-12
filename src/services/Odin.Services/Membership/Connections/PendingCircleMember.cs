using System;
using Odin.Core.Identity;
using Odin.Core.Time;

namespace Odin.Services.Membership.Connections;

/// <summary>
/// An identity an app has been asked to add to a circle, whose grant is deposited but not yet converted
/// -- so they are not a member of it yet.
/// </summary>
/// <remarks>
/// A read-bearing grant escrows the drive's storage key under the connection's Peer Key, which an app
/// cannot reach; it deposits the grant sealed to the connection's write-only public key instead, and the
/// grant becomes real the next time the Peer Key is in scope -- the contact's next inbound request, the
/// owner touching that connection, or the deposit-conversion pass in the version upgrade.
/// <para>
/// Reported beside the real members rather than among them, because they genuinely are not members: the
/// permission is recorded but nothing is granted yet.  The two extra fields are here so a UI can say
/// <i>why</i> someone is missing rather than merely that they are -- who asked, and how long ago.
/// </para>
/// </remarks>
public class PendingCircleMember
{
    public OdinId OdinId { get; init; }

    /// <summary>When the grant was deposited.</summary>
    public UnixTimeUtc Deposited { get; init; }

    /// <summary>The app whose client deposited it; null if it was not deposited by an app.</summary>
    public Guid? DepositingAppId { get; init; }
}
