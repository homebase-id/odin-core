using System.Collections.Generic;
using Odin.Core.Time;

namespace Odin.Services.ShamiraPasswordRecovery;

/// <summary>
/// Contains shard information retained by the dealer
/// </summary>
public class DealerShardPackage
{
    public int MinMatchingShards { get; init; }
    public List<DealerShardEnvelope> Envelopes { get; init; }
    public UnixTimeUtc Created { get; set; }
}