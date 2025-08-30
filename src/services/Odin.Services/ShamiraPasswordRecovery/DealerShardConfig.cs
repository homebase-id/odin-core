
using System.Collections.Generic;
using Odin.Core.Time;

namespace Odin.Services.ShamiraPasswordRecovery;

public class DealerShardConfig
{
    public int MinMatchingShards { get; init; }
    public List<DealerShardEnvelopeRedacted> Envelopes { get; init; } = new();
    public UnixTimeUtc Created { get; init; }
}