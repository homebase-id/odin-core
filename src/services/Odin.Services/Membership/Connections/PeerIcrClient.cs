using System;
using System.Text.Json.Serialization;
using Odin.Core;
using Odin.Core.Identity;
using Odin.Core.Serialization;
using Odin.Core.Time;
using Odin.Services.Authorization.ExchangeGrants;

namespace Odin.Services.Membership.Connections;

/// <summary>
/// Access registration based on the ICR; originally created to support websocket App notifications
/// </summary>
public sealed class PeerIcrClient : IClientRegistration
{
    public PeerIcrClient()
    {
        // for json
    }

    public OdinId Identity { get; init; }

    /// <summary>
    /// When the connection this token was minted under was made; a token from an earlier
    /// connection with the same identity is refused. Zero on tokens minted before this was recorded.
    /// </summary>
    public UnixTimeUtc ConnectionCreated { get; init; }

    [JsonPropertyName("accessRegistration")]
    public ServerHalfOfClientKey ServerHalfOfClientKey { get; init; }

    public Guid Id => ServerHalfOfClientKey!.Id;

    public string IssuedTo => this.Identity;

    public int Type => 300;

    /// <summary>Restarted on each use (<see cref="CircleNetworkStorage.GetPeerIcrClientAsync"/>), so only an idle token runs out</summary>
    public int TimeToLiveSeconds => (int)TimeSpan.FromDays(30).TotalSeconds;

    public Guid CategoryId => Guid.Parse("87ae004a-27db-42e8-850e-35effd8cca1d");

    public string GetValue()
    {
        return OdinSystemSerializer.Serialize(this);
    }
}