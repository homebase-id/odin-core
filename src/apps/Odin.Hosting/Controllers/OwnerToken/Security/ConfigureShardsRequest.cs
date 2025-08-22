using System;
using System.Collections.Generic;
using Odin.Core.Identity;
using Odin.Services.ShamiraPasswordRecovery;

namespace Odin.Hosting.Controllers.OwnerToken.Security;

public class ConfigureShardsRequest
{
    public List<ShamiraPlayer> Players { get; set; }
    public int MinMatchingShards { get; set; }
}

public class VerifyRemotePlayerShardRequest
{
    public OdinId OdinId { get; set; }
    
    public Guid ShardId { get; set; }

}