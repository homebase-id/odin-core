
using System.Collections.Generic;
using Odin.Core.Time;

namespace Odin.Services.ShamiraPasswordRecovery;

public class DealerShardConfig
{
    public List<DealerShardEnvelopeRedacted> Envelopes { get; init; } = new();
    public UnixTimeUtc Created { get; init; }
}