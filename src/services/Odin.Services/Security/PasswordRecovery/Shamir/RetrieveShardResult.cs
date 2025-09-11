using System;

namespace Odin.Services.Security.PasswordRecovery.Shamir;

public enum RetrieveShardResultType
{
    /// <summary>
    ///  The shard type was automatic so we just send the shard back
    /// </summary>
    Complete,
    
    /// <summary>
    ///  The player must send the shard
    /// </summary>
    WaitingForPlayer,
}

public class RetrieveShardResult
{
    public RetrieveShardResultType ResultType { get; init; }
    public PlayerEncryptedShard Shard { get; init; }
}

public class RetrieveShardRequest
{
    public Guid ShardId { get; init; }
    public Guid HashedRecoveryEmail { get; init; }
}

public class ApproveShardResult
{
    public Guid ShardId { get; init; }

}