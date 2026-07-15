using System;
using System.Text.Json.Serialization;
using Odin.Core;
using Odin.Core.Serialization;
using Odin.Core.Util;
using Odin.Services.Authorization.ExchangeGrants;

namespace Odin.Services.Membership.YouAuth;

public sealed class YouAuthDomainClient : IClientRegistration
{
    public YouAuthDomainClient()
    {
        // for json
    }

    public YouAuthDomainClient(AsciiDomainName domain, string friendlyName, ServerHalfOfClientKey serverHalfOfClientKey)
    {
        Domain = domain;
        ServerHalfOfClientKey = serverHalfOfClientKey;
        FriendlyName = friendlyName;
    }

    public AsciiDomainName Domain { get; init; }

    [JsonPropertyName("accessRegistration")]
    public ServerHalfOfClientKey ServerHalfOfClientKey { get; init; }

    public string FriendlyName { get; init; }

    public Guid Id => this.ServerHalfOfClientKey.Id;

    public string IssuedTo => this.Domain.DomainName;
    public int Type => 408;
    public int TimeToLiveSeconds { get; set; } 
    public Guid CategoryId { get; } = Guid.Parse("83742ae7-e66d-45e6-82a6-6a003c960b39");

    public string GetValue()
    {
        return OdinSystemSerializer.Serialize(this);
    }
}