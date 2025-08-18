using System;

namespace Odin.Services.ShamiraPasswordRecovery;

public class VerifyShardRequest
{
    public Guid ShardId { get; set; }
}

public class ShardVerificationResult
{
    public bool IsValid { get; set; }
}