using System.Collections.Generic;
using Odin.Services.ShamiraPasswordRecovery;

namespace Odin.Hosting.Controllers.OwnerToken.Security;

public class ConfigureShardsRequest
{
    public List<ShamiraPlayer> Players { get; set; }
    public int TotalShards { get; set; }
    public int MinMatchingShards { get; set; }
}