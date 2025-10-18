using System;
using Odin.Core;
using Odin.Core.Serialization;
using Odin.Core.Util;
using Odin.Services.Authorization.ExchangeGrants;

namespace Odin.Services.Membership.YouAuth;

public sealed class YouAuthDomainClient(AsciiDomainName domain, string friendlyName, AccessRegistration accessRegistration)
    : IClientRegistration
{
    public AsciiDomainName Domain { get; init; } = domain;
    public AccessRegistration AccessRegistration { get; init; } = accessRegistration;
    public string FriendlyName { get; init; } = friendlyName;

    public Guid Id => this.AccessRegistration.Id;

    public string IssuedTo => this.Domain.DomainName;
    public int Type => 408;
    public long TimeToLiveSeconds { get; set; } 
    public Guid CategoryId { get; } = Guid.Parse("83742ae7-e66d-45e6-82a6-6a003c960b39");

    public string GetValue()
    {
        return OdinSystemSerializer.Serialize(this);
    }
}