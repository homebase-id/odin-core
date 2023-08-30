using System.Collections.Generic;
using Odin.Core.Util;

namespace Odin.Core.Services.Membership.YouAuth;

public class UpdateYouAuthDomainPermissionsRequest
{
    public AsciiDomainName Domain { get; set; }

    /// <summary>
    /// The circles to be granted to the domain
    /// </summary>
    public List<GuidId> CircleIds { get; set; }

}