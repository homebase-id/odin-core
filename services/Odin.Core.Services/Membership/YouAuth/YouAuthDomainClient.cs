using System;
using Dawn;
using Odin.Core.Services.Authorization.ExchangeGrants;
using Odin.Core.Util;

namespace Odin.Core.Services.Membership.YouAuth;

public sealed class YouAuthDomainClient
{
    public YouAuthDomainClient(AsciiDomainName domain, string friendlyName, AccessRegistration accessRegistration)
    {
        Guard.Argument(domain, nameof(domain)).Require(x => !string.IsNullOrEmpty(x.DomainName));
        Guard.Argument(accessRegistration, nameof(accessRegistration)).NotNull();

        Domain = domain;
        FriendlyName = friendlyName;
        AccessRegistration = accessRegistration;
    }

    public AsciiDomainName Domain { get; init; }

    public AccessRegistration AccessRegistration { get; init; }

    public string FriendlyName { get; init; }
    
}

public class RedactedYouAuthDomainClient
{
    public AsciiDomainName Domain { get; set; }

    public GuidId AccessRegistrationId { get; set; }

    public string FriendlyName { get; set; }

    public AccessRegistrationClientType AccessRegistrationClientType { get; set; }

    public Int64 Created { get; set; }

    public bool IsRevoked { get; set; }
}