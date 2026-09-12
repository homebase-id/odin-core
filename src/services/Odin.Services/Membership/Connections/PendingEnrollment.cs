using System;
using Odin.Core;
using Odin.Core.Time;

namespace Odin.Services.Membership.Connections;

/// <summary>
/// A circle the owner chose for a connection that the caller could not grant, recorded so the app that
/// can grant it may finish the job later.
/// </summary>
/// <remarks>
/// Distinct from <see cref="DepositedGrant"/>, and the difference is which key was missing.  A deposit is
/// made by a caller that <i>could</i> source every storage key the circle needs but could not reach the
/// connection's Peer Key; it carries real sealed key material and only awaits conversion.  This carries
/// no key material at all: it is made by a caller that could not source the keys in the first place --
/// the chat client asked to enrol someone in the mail app's circle, and only the mail app holds the
/// storage keys for mail's drives.
/// <para>
/// Recording it grants nothing.  Whoever processes it re-checks scope at that point, so an entry is a
/// statement of the owner's intent, never of authority: an app cannot widen its own reach by enqueuing
/// something it is not allowed to do.
/// </para>
/// </remarks>
public class PendingEnrollment
{
    public GuidId CircleId { get; set; } = null!;

    /// <summary>
    /// The app that owns the circle, and so the only app that can complete this.  Denormalised from the
    /// circle definition so the "has this app any work?" lookup does not have to load every definition;
    /// safe to copy because ownership is fixed when a circle is created and cannot be reassigned.
    /// Null for an owner circle, which only the owner can complete.
    /// </summary>
    public Guid? OwningAppId { get; set; }

    /// <summary>Provenance: the app whose client recorded the request.</summary>
    public Guid? RequestedByAppId { get; set; }

    public UnixTimeUtc Requested { get; set; }
}
