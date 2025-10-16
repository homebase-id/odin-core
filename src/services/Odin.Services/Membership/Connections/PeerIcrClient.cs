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

    public Guid Id
    {
        get => AccessRegistration!.Id;
        set { }
    }

    public OdinId IssuedTo
    {
        get => this.Identity;
        set { }
    }

    public int Type
    {
        get => 300;
        set { }
    }

    public long TimeToLiveSeconds
    {
        get => 60 * 60 * 3; // 3 hours
        set { }
    }

    public string GetValue()
    {
        return OdinSystemSerializer.Serialize(this);
    }
}