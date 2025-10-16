using Odin.Core.Util;
using Odin.Services.Authorization.ExchangeGrants;

namespace Odin.Services.Membership.YouAuth;


public sealed class YouAuthDomainClient(AsciiDomainName domain, string friendlyName, AccessRegistration accessRegistration)
{
    public AsciiDomainName Domain { get; init; } = domain;

    public AccessRegistration AccessRegistration { get; init; } = accessRegistration;

    public string FriendlyName { get; init; } = friendlyName;
}