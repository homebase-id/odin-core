using Odin.Core.Identity;
using Odin.Services.Authorization.ExchangeGrants;

namespace Odin.Services.Membership.Connections;

/// <summary>
/// Access registration based on the ICR; originally created to support websocket App notifications
/// </summary>
public sealed class PeerIcrClient
{
    public OdinId Identity { get; init; }

    public AccessRegistration AccessRegistration { get; init; }
}