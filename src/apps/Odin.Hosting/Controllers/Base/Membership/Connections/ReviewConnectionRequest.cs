using System.Collections.Generic;
using Odin.Core;

namespace Odin.Hosting.Controllers.Base.Membership.Connections;

/// <summary>
/// Completes the connection review.  An empty or omitted <see cref="CircleIds"/> is the "chat only"
/// outcome: it records that the owner looked and decided, and grants nothing.
/// </summary>
public class ReviewConnectionRequest
{
    public string OdinId { get; set; }

    public IEnumerable<GuidId> CircleIds { get; set; }
}
