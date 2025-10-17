using System;
using Odin.Core;
using Odin.Core.Identity;
using Odin.Core.Serialization;
using Odin.Services.Authorization.ExchangeGrants;

namespace Odin.Services.Authorization.Apps;

/// <summary>
/// The server-side half of a <see cref="ClientAuthenticationToken"/>
/// </summary>
public sealed class AppClientRegistration : IClientRegistration
{
    public const int CatType = 200;
    public AppClientRegistration(GuidId appId, string friendlyName, AccessRegistration accessRegistration)
    {
        GuidId.AssertIsValid(appId);

        AppId = appId;
        FriendlyName = friendlyName;
        AccessRegistration = accessRegistration;
    }

    public GuidId AppId { get; init; }
    public AccessRegistration AccessRegistration { get; init; }
    public string FriendlyName { get; init; }

    public Guid Id
    {
        get => this.AccessRegistration.Id;
        set { }
    }

    public OdinId IssuedTo { get; set; }
    
    public int Type
    {
        get => CatType;
        set { }
    }

    public long TimeToLiveSeconds
    {
        get => 60 * 60 * 3; // 3 hours
        set { }
    }
    
    public Guid CategoryId
    {
        get => this.AppId;
        set { }
    }

    public string GetValue()
    {
        return OdinSystemSerializer.Serialize(this);
    }
}