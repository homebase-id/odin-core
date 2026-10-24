using System;
using System.Collections.Generic;
using Odin.Core.Identity;
using Odin.Services.Security.PasswordRecovery.Shamir;

namespace Odin.Hosting.Controllers.OwnerToken.Security;

public class VerifyRemotePlayerShardRequest
{
    public OdinId OdinId { get; init; }
    public Guid ShardId { get; init; }
}

public class VerifyRemotePlayerReadinessRequest
{
    public OdinId OdinId { get; init; }
}

public class ApproveShardRequest
{
    public OdinId OdinId { get; init; }
    public Guid ShardId { get; init; }

    /// <summary>The shard's index, from the opened shard.</summary>
    public int Index { get; init; }

    /// <summary>The shard's piece, sealed to the recovering browser's throwaway key.</summary>
    public byte[] SealedShard { get; init; }
}

public class RejectShardRequest
{
    public OdinId OdinId { get; init; }
    public Guid ShardId { get; init; }
}