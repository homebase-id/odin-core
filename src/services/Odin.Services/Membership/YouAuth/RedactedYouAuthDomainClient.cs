using Odin.Core;
using Odin.Core.Time;
using Odin.Core.Util;
using Odin.Services.Authorization.ExchangeGrants;

namespace Odin.Services.Membership.YouAuth;

public class RedactedYouAuthDomainClient
{
    public AsciiDomainName Domain { get; set; }

    public GuidId AccessRegistrationId { get; set; }

    public string FriendlyName { get; set; }

    public AccessRegistrationClientType AccessRegistrationClientType { get; set; }

    public UnixTimeUtc Created { get; set; }

    public bool IsRevoked { get; set; }
}