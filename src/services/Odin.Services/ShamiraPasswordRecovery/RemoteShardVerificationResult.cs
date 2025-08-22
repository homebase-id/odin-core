using System.Collections.Generic;

namespace Odin.Services.ShamiraPasswordRecovery;

public class RemoteShardVerificationResult
{
    public Dictionary<string, ShardVerificationResult> Players { get; init; } = new();
}

public class ShardVerificationResult
{
    public bool IsValid { get; init; }
}