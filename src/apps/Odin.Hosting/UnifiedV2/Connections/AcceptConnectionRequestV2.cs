using System.Collections.Generic;
using Odin.Core;

namespace Odin.Hosting.UnifiedV2.Connections;

/// <summary>
/// Body for PUT /requests/incoming/{senderId}: the circles granted to the sender when
/// accepting their connection request. The sender is identified by the route, not the body.
/// </summary>
public class AcceptConnectionRequestV2
{
    public IEnumerable<GuidId> CircleIds { get; set; }
}
