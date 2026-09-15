using System;
using System.Collections.Generic;

namespace Odin.Hosting.Controllers.Base.Membership.Connections
{
    /// <summary>
    /// Adds several identities to one circle.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="AddCircleMembershipRequest"/> rather than making that one take a list:
    /// the single-add endpoint throws when the identity cannot be added, and callers rely on that,
    /// whereas a bulk add has to report per-identity outcomes instead of failing the batch.
    /// </remarks>
    public class AddManyCircleMembershipRequest
    {
        public Guid CircleId { get; set; }

        public List<string> OdinIds { get; set; } = [];
    }
}
