using System.Collections.Generic;
using Odin.Core;

namespace Odin.Hosting.Controllers.Base.Membership.Connections;

/// <summary>
/// The owner's review of a connection: who, and the circles they chose in the review dialog.
/// </summary>
/// <remarks>
/// One atomic act -- the circles are enrolled and the review stamped in a single call
/// (docs/connection-defaults.md, "On verify").  <see cref="CircleIds"/> may be empty: reviewing without
/// granting anything is the "Chat only" case, where every toggle was declined and the contact keeps
/// exactly what auto-connect already gave them.
/// </remarks>
public class MarkConnectionReviewedRequest
{
    public string OdinId { get; set; }

    public IEnumerable<GuidId> CircleIds { get; set; }
}
