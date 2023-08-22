using System.Collections.Generic;
using Odin.Core.Services.Authorization.ExchangeGrants;
using Odin.Core.Services.Authorization.Permissions;
using Odin.Core.Util;

namespace Odin.Core.Services.Authorization.YouAuth;

public class UpdateYouAuthDomainPermissionsRequest
{
    public AsciiDomainName Domain { get; set; }

    /// <summary>
    /// The circles to be granted to the domain
    /// </summary>
    public List<GuidId> CircleIds { get; set; }

}