using System.Collections.Generic;
using Odin.Core.Time;

namespace Odin.Services.ShamiraPasswordRecovery;

public class ShamirRecoveryStatusRecord
{
    public UnixTimeUtc Updated { get; init; }
    public ShamirRecoveryState State { get; init; }
    public List<PlayerEncryptedShard> CollectedShards { get; init; } = new();
}


public class ShamirRecoveryStatusRedacted
{
    public UnixTimeUtc Updated { get; init; }
    public ShamirRecoveryState State { get; init; }
    public string Email { get; init; }
}

