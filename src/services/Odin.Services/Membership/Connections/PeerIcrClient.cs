using System;
using Odin.Core;
using Odin.Core.Identity;
using Odin.Core.Serialization;
using Odin.Services.Authorization.ExchangeGrants;

namespace Odin.Services.Membership.Connections;

/// <summary>
/// Access registration based on the ICR; originally created to support websocket App notifications
/// </summary>
public sealed class PeerIcrClient : IClientRegistration
{
    public OdinId Identity { get; init; }

    public AccessRegistration AccessRegistration { get; init; }

    public Guid Id => AccessRegistration!.Id;

    public string IssuedTo => this.Identity;

    public int Type => 300;

    public long TimeToLiveSeconds => (long)TimeSpan.FromDays(365).TotalSeconds;

    public Guid CategoryId { get; set; }

    public string GetValue()
    {
        return OdinSystemSerializer.Serialize(this);
    }
}