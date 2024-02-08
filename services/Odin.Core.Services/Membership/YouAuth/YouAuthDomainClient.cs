using System;
using Dawn;
using Odin.Core.Services.Authorization.ExchangeGrants;
using Odin.Core.Util;

namespace Odin.Core.Services.Membership.YouAuth;

public class YouAuthDomainClientRegistrationResponse
{
    public Guid AccessRegistrationId { get; set; }
    /// <summary>
    /// RSA encrypted response.  When encryption version == 1, the  first 16 bytes is token id, second 16 bytes is AccessTokenHalfKey, and last 16 bytes is SharedSecret
    /// </summary>
    public byte[] Data { get; set; }
}

public sealed class YouAuthDomainClient
{
    public YouAuthDomainClient(AsciiDomainName domain, string friendlyName, AccessRegistration accessRegistration)
    {
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