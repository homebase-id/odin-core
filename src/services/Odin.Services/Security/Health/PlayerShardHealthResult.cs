using System;
using Odin.Services.Security.PasswordRecovery.Shamir;

namespace Odin.Services.Security.Health;

public class PlayerShardHealthResult
{
    public ShamiraPlayer Player { get; set; }
    public bool IsValid { get; set; }
    public ShardTrustLevel TrustLevel { get; set; }

    /// <summary>
    /// True if the dealer expects this player but no health check data was found.
    /// </summary>
    public bool IsMissing { get; set; }

    /// <summary>
    /// False when a delegate is no longer connected to the dealer, so cannot deliver its shard
    /// during recovery (#1885). True for automated players, and for results stored before this existed.
    /// </summary>
    public bool IsConnected { get; set; } = true;

    /// <summary>
    /// The shard id in question as held by PlayerId
    /// </summary>
    public Guid ShardId { get; set; }
}
