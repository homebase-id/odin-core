using System;

namespace Odin.Services.Security.PasswordRecovery.Shamir;

public enum RetrieveShardResultType
{
    /// <summary>
    ///  The shard is in this result, sealed to the recovering browser's key
    /// </summary>
    Complete,
    
    /// <summary>
    ///  The player must send the shard
    /// </summary>
    WaitingForPlayer,
}

public class RetrieveShardResult
{
    public RetrieveShardResultType ResultType { get; init; }
    public PlayerEncryptedShard Shard { get; init; }
}

public class RetrieveShardRequest
{
    public Guid ShardId { get; init; }
    public Guid HashedRecoveryEmail { get; init; }

    /// <summary>
    /// The recovering browser's throwaway public key: a gated friend's browser seals their shard to it
    /// when they approve, and an automatic holder when the owner clicks its email's link.
    /// </summary>
    public string RecoveryPublicKeyJwk { get; init; }

    /// <summary>
    /// For an automatic holder only: the recovery address to email the link to.  The holder checks it
    /// against the hash it stored at the deal, so a dealer can't redirect the mail.
    /// </summary>
    public string RecoveryEmail { get; init; }
}

/// <summary>
/// The code from an automatic holder's email, which the owner's click brought back to the dealer: the
/// dealer passes it to the holder (<c>release-automatic-shard</c>).
/// </summary>
public class ReleaseAutomaticShardRequest
{
    public Guid ShardId { get; init; }
    public string Code { get; init; }
}

public class ApproveShardResult
{
    public Guid ShardId { get; init; }

}