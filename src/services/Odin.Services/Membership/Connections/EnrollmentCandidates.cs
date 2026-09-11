using System;
using System.Collections.Generic;
using Odin.Core.Identity;
using Odin.Services.Membership.Circles;

namespace Odin.Services.Membership.Connections;

/// <summary>
/// The connections that could be added to one of an app's circles but are not in it yet.
/// </summary>
/// <remarks>
/// Assigning a circle to an app does not reach back over the contacts the owner already reviewed:
/// a review is a moment, not a standing rule, and at that moment this circle either did not exist
/// or was not the app's.  So the backlog is real and invisible.  Reporting it per circle, rather
/// than acting on it, is the whole point -- the owner decides, from the app's own page, whenever
/// they happen to be there.
/// </remarks>
public sealed class CircleEnrollmentCandidates
{
    public Guid CircleId { get; init; }

    public string CircleName { get; init; } = "";

    /// <summary>
    /// Why these identities qualify.  Carried so a client can say "your reviewed contacts" rather
    /// than the uselessly vague "some contacts".
    /// </summary>
    public CircleGrantOn GrantOn { get; init; }

    public List<OdinId> Candidates { get; init; } = [];
}

/// <summary>
/// What a bulk enrolment actually did.
/// </summary>
/// <remarks>
/// Three outcomes rather than a count, because they mean different things to the owner: a grant is
/// membership now, a deposit is membership once the connection's Peer Key is next in scope, and a
/// skip is someone who stopped qualifying between the page being drawn and the button being
/// pressed.  Reporting only a total would make the second look like the first.
/// </remarks>
public sealed class EnrollmentResult
{
    /// <summary>Became members outright.</summary>
    public int Enrolled { get; set; }

    /// <summary>Grant recorded but not yet in effect; awaits the Peer Key.</summary>
    public int Deposited { get; set; }

    /// <summary>No longer eligible, or already handled.</summary>
    public int Skipped { get; set; }
}
