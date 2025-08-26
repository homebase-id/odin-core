using System;

namespace Odin.Services.ShamiraPasswordRecovery;

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
    public RetrieveShardResultType ResultType { get; set; }
    public PlayerEncryptedShard Shard { get; set; }
}

public class RetrieveShardRequest
{
    public Guid ShardId { get; init; }
}