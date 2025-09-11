using System.Collections.Generic;
using Odin.Core.Time;

namespace Odin.Services.Security;

public class PeriodicSecurityHealthCheckStatus
{
    public UnixTimeUtc LastUpdated { get; set; }
    public bool IsConfigured { get; init; }
    public List<PlayerShardHealthResult> Players { get; set; } = new();
}