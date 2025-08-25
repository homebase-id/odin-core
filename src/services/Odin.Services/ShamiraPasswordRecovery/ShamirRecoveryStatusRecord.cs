using Odin.Core.Time;

namespace Odin.Services.ShamiraPasswordRecovery;

public class ShamirRecoveryStatusRecord
{
    public UnixTimeUtc Updated { get; init; }
    public ShamirRecoveryState State { get; init; }
}


public class ShamirRecoveryStatusRedacted
{
    public UnixTimeUtc Updated { get; init; }
    public ShamirRecoveryState State { get; init; }
    public string Email { get; init; }
}

