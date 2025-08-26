using System;
using System.Collections.Generic;
using Odin.Core.Identity;
using Odin.Services.ShamiraPasswordRecovery;

namespace Odin.Hosting.Controllers.OwnerToken.Security;

public class ConfigureShardsRequest
{
    public List<ShamiraPlayer> Players { get; init; }
    public int MinMatchingShards { get; init; }
}

public class VerifyRemotePlayerShardRequest
{
    public OdinId OdinId { get; init; }
    
    public Guid ShardId { get; init; }

}