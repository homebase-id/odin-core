using System;
using Odin.Core.Identity;
using Odin.Core.Time;

namespace Odin.Services.ShamiraPasswordRecovery.ShardRequestApproval;

/// <summary>
/// Record when a player has requested a shard that needs to be approved
/// </summary>
public class ShardApprovalRequest
{
    public Guid Id { get; init; }
    public OdinId Player { get; init; }
    public UnixTimeUtc Created { get; init; }
}