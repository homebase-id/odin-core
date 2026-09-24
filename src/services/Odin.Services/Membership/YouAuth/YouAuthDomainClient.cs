using System;
using System.Text.Json.Serialization;
using Odin.Core;
using Odin.Core.Cryptography.Data;
using Odin.Core.Serialization;
using Odin.Core.Time;
using Odin.Core.Util;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Base;

namespace Odin.Services.Membership.YouAuth;

/// <summary>
/// The server-side half of a token issued to a YouAuth domain: one row in the client-registrations
/// table per login.
/// </summary>
/// <remarks>
/// How long it lives is the owner's call, made when they consented to the domain
/// (<see cref="ConsentRequirements"/>), and <see cref="Create"/> is the one place that reads that
/// choice:
/// <list type="bullet">
/// <item><b>Expiring</b>, with a date: the token expires on that date. Use never moves it.</item>
/// <item><b>Never</b> or <b>Always</b>: <see cref="SlidingLifetime"/> from issue, restarted by every
/// use, so a client in regular use is never cut off and one that went quiet is gone six months later.
/// The same window the owner console session has. Always says "ask me on every login", which is
/// about consent, not about how long the token issued after that consent lives.</item>
/// </list>
/// </remarks>
public sealed class YouAuthDomainClient : IClientRegistration
{
    public const int CatType = 408;

    /// <summary>
    /// The owner console session's window: a domain the owner gave no end date to is treated the
    /// way their own login is.
    /// </summary>
    public static readonly TimeSpan SlidingLifetime = OwnerConsoleClientRegistration.Lifetime;

    /// <summary>
    /// One category for every domain client; a per-domain lookup goes by <see cref="IssuedTo"/>
    /// (<see cref="Authorization.ClientRegistrationStorage.GetByTypeAndIssuedToAsync{T}"/>).
    /// </summary>
    public static readonly Guid CategoryIdValue = Guid.Parse("83742ae7-e66d-45e6-82a6-6a003c960b39");

    public YouAuthDomainClient()
    {
        // for json
    }

    /// <summary>
    /// Sizes the token to the owner's consent choice.
    /// </summary>
    public static YouAuthDomainClient Create(
        AsciiDomainName domain,
        string friendlyName,
        ServerHalfOfClientKey serverHalfOfClientKey,
        ConsentRequirements consent)
    {
        var fixedDate = consent?.ConsentRequirementType == ConsentRequirementType.Expiring;
        var lifetime = fixedDate
            ? consent.Expiration - UnixTimeUtc.Now()
            : SlidingLifetime;

        return new YouAuthDomainClient
        {
            Domain = domain,
            FriendlyName = friendlyName,
            ServerHalfOfClientKey = serverHalfOfClientKey,
            TimeToLiveSeconds = (int)Math.Max(0, lifetime.TotalSeconds),
            SlidingExpiration = !fixedDate
        };
    }

    public AsciiDomainName Domain { get; init; }

    [JsonPropertyName("accessRegistration")]
    public ServerHalfOfClientKey ServerHalfOfClientKey { get; init; }

    public string FriendlyName { get; init; }

    public Guid Id => this.ServerHalfOfClientKey.Id;

    public string IssuedTo => this.Domain.DomainName;
    public int Type => CatType;

    public int TimeToLiveSeconds { get; init; }

    /// <summary>
    /// True when each use restarts <see cref="TimeToLiveSeconds"/>; false when the owner named the
    /// date and it stays put.
    /// </summary>
    public bool SlidingExpiration { get; init; }

    public Guid CategoryId => CategoryIdValue;

    public string GetValue()
    {
        return OdinSystemSerializer.Serialize(this);
    }
}
