using System.Collections.Generic;

namespace Odin.Services.ShamiraPasswordRecovery;

/// <summary>
/// Contains shard information retained by the dealer
/// </summary>
public class DealerShardPackage
{
    public List<DealerShardEnvelope> Envelopes { get; set; }
}