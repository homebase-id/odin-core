using System.Collections.Generic;
using Odin.Core;
using Odin.Core.Util;

namespace Odin.Services.Membership.YouAuth;

public class UpdateYouAuthDomainPermissionsRequest
{
    public AsciiDomainName Domain { get; set; }

    /// <summary>
    /// The circles to be granted to the domain
    /// </summary>
    public List<GuidId> CircleIds { get; set; }

}