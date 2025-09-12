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
    /// The shard id in question as held by PlayerId
    /// </summary>
    public Guid ShardId { get; set; }
}
