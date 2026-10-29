using System;
using Odin.Core;
using Odin.Core.Identity;
using Odin.Core.Serialization;
using Odin.Core.Time;

namespace Odin.Services.Security.PasswordRecovery.Shamir.ShardRequestApproval;

/// <summary>
/// Record when a player has requested a shard that needs to be approved
/// </summary>
public class ShardApprovalRequest
{
    public Guid ShardId { get; init; }
    public OdinId Dealer { get; init; }
    public UnixTimeUtc Created { get; init; }

    /// <summary>
    /// The recovering browser's throwaway public key: approving seals the shard to it, so only that
    /// browser opens it.
    /// </summary>
    public string RecoveryPublicKeyJwk { get; init; }

    public static string Serialize(ShardApprovalRequest shard)
    {
        return OdinSystemSerializer.Serialize(shard).ToUtf8ByteArray().ToBase64();
    }
    
    public static ShardApprovalRequest Deserialize(string content)
    {
        return OdinSystemSerializer.Deserialize<ShardApprovalRequest>(content.FromBase64().ToStringFromUtf8Bytes());
    }
}