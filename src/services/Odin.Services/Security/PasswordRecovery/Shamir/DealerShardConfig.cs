using System.Collections.Generic;
using Odin.Core.Time;

namespace Odin.Services.Security.PasswordRecovery.Shamir;

public class DealerShardConfig
{
    public int MinMatchingShards { get; init; }
    public List<DealerShardEnvelopeRedacted> Envelopes { get; init; } = new();
    public UnixTimeUtc Updated { get; init; }
    public bool UsesAutomaticRecovery { get; set; }
}