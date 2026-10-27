using System;
using System.Text.Json.Serialization;
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

    public AppClientRegistration(OdinId ownerOdinId, GuidId appId, string friendlyName, ServerHalfOfClientKey serverHalfOfClientKey)
    {
        GuidId.AssertIsValid(appId);

        AppId = appId;
        FriendlyName = friendlyName;
        ServerHalfOfClientKey = serverHalfOfClientKey;
        IssuedTo = ownerOdinId;
    }

    public GuidId AppId { get; init; }

    [JsonPropertyName("accessRegistration")]
    public ServerHalfOfClientKey ServerHalfOfClientKey { get; init; }
    
    public string FriendlyName { get; init; }

    public Guid Id => this.ServerHalfOfClientKey.Id;

    public string IssuedTo { get; set; }

    /// <summary>
    /// Whether this device was handed the app's zero-knowledge key (AppZkK) when its token was made, so it can
    /// open the keys of the zero-knowledge drives the app is granted (docs/zero-knowledge-drive-api-plan.md,
    /// step 3).  Set once, at sign-in, by an app that asked for it.
    /// </summary>
    public bool HoldsZkAppKey { get; init; }

    public int Type => CatType;

    public int TimeToLiveSeconds => (int)TimeSpan.FromDays(365).TotalSeconds; // 3 hours

    public Guid CategoryId => this.AppId;

    public string GetValue()
    {
        return OdinSystemSerializer.Serialize(this);
    }
}