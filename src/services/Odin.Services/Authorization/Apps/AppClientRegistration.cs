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

    public AppClientRegistration()
    {
        // for json
    }

    public AppClientRegistration(OdinId ownerOdinId, GuidId appId, string friendlyName, AccessRegistration accessRegistration)
    {
        GuidId.AssertIsValid(appId);

        AppId = appId;
        FriendlyName = friendlyName;
        AccessRegistration = accessRegistration;
        IssuedTo = ownerOdinId;
    }

    public GuidId AppId { get; init; }
    public AccessRegistration AccessRegistration { get; init; }
    public string FriendlyName { get; init; }

    public Guid Id => this.AccessRegistration.Id;

    public string IssuedTo { get; set; }

    public int Type => CatType;

    public int TimeToLiveSeconds => (int)TimeSpan.FromDays(365).TotalSeconds; // 3 hours

    public Guid CategoryId => this.AppId;

    public string GetValue()
    {
        return OdinSystemSerializer.Serialize(this);
    }
}