using System;
using System.Collections.Generic;
using System.Linq;
using Odin.Core.Exceptions;
using Odin.Services.Peer.Encryption;
using Odin.Services.Security.ZeroKnowledge;
using Odin.Services.Util;

namespace Odin.Services.Security.PasswordRecovery.Shamir;

/// <summary>
/// A friend deal, made in the browser (docs/zero-knowledge-recovery-plan.md, "Building the friends
/// pair"): the browser does what <c>CreateShards</c> does for an automated deal, plus sealing each gated
/// friend's shard to them, and the server sends the shards and keeps the dealer package as it does for
/// an automated deal.  The secret the shards rebuild never reaches the server.
/// </summary>
public sealed class ConfigureShardsRequest
{
    public int MinMatchingShards { get; init; }

    public List<DealtShard> Shards { get; init; }

    public ZkShamirCopiesRequest ShamirCopies { get; init; }

    /// <summary>The checks that need nothing but the request.</summary>
    public void Validate()
    {
        OdinValidationUtils.AssertNotNull(Shards, nameof(Shards));
        OdinValidationUtils.AssertNotNull(ShamirCopies, nameof(ShamirCopies));
        ShamirCopies.Validate();

        OdinValidationUtils.AssertIsTrue(Shards.Count >= ShamirConfigurationService.MinimumPlayerCount,
            $"You need at least {ShamirConfigurationService.MinimumPlayerCount} trusted connections");
        OdinValidationUtils.AssertIsTrue(MinMatchingShards >= 1 && MinMatchingShards <= Shards.Count,
            "The minimum number of shards must be between 1 and the number of players");
        OdinValidationUtils.AssertIsTrue(
            Shards.Select(s => s.Player?.OdinId).Distinct().Count() == Shards.Count &&
            Shards.Select(s => s.ShardId).Distinct().Count() == Shards.Count &&
            Shards.Select(s => s.Index).Distinct().Count() == Shards.Count,
            "Each shard needs its own player, id and index");

        foreach (var shard in Shards)
        {
            shard.Validate();
        }

        // The automatic shards are released by servers with no human, as is the dealer package, so
        // reaching the threshold with them alone would let servers rebuild the secret, and so open the
        // zero-knowledge master key.  At least one gated friend must always approve (F2).
        var automatic = Shards.Count(s => s.Player.Type == PlayerType.Automatic);
        if (automatic >= MinMatchingShards)
        {
            throw new OdinClientException(
                $"{automatic} automatic shard(s) reach the threshold of {MinMatchingShards}; at least one friend must approve",
                OdinClientErrorCode.AutomaticShardsReachThreshold);
        }
    }
}

/// <summary>
/// One player's shard, as the browser dealt it: a piece of the secret, locked by the shard's own key
/// (<see cref="EncryptionKey"/>, AES-CBC, which the dealer package keeps, as today).
/// </summary>
public sealed class DealtShard
{
    public ShamiraPlayer Player { get; init; }
    public Guid ShardId { get; init; }

    /// <summary>The piece's x in the Shamir split, 1 to the number of players.</summary>
    public int Index { get; init; }

    /// <summary>The shard's key, kept in the dealer package: 16 bytes.</summary>
    public byte[] EncryptionKey { get; init; }

    public byte[] EncryptionIv { get; init; }

    /// <summary>
    /// An automatic friend's shard: the piece locked by <see cref="EncryptionKey"/>, as today's
    /// <see cref="PlayerEncryptedShard.DealerEncryptedShard"/>.  Their server releases it with no human.
    /// </summary>
    public byte[] DealerEncryptedShard { get; init; }

    /// <summary>
    /// A gated friend's shard: the file's content as the friend's drive stores it, base64 of today's
    /// <see cref="PlayerEncryptedShard"/> JSON, AES-CBC encrypted with the file's own key.  Only the
    /// friend's browser opens it.
    /// </summary>
    public string DepositContent { get; init; }

    /// <summary>The file's key, sealed to the friend's Shard Recovery write-only public key.</summary>
    public EncryptedKeyHeader DepositKeyHeader { get; init; }

    public bool IsGated => Player.Type == PlayerType.Delegate;

    public void Validate()
    {
        OdinValidationUtils.AssertNotNull(Player, nameof(Player));
        OdinValidationUtils.AssertNotEmptyGuid(ShardId, nameof(ShardId));
        OdinValidationUtils.AssertIsTrue(Index >= 1, "A shard's index starts at 1");
        OdinValidationUtils.AssertIsTrue(EncryptionKey?.Length == 16 && EncryptionIv?.Length == 16,
            "A shard's key and IV are 16 bytes each");

        switch (Player.Type)
        {
            case PlayerType.Automatic:
                OdinValidationUtils.AssertIsTrue(DealerEncryptedShard?.Length > 0 && DepositContent == null && DepositKeyHeader == null,
                    $"An automatic shard for {Player.OdinId} is sent as is, not sealed");
                break;
            case PlayerType.Delegate:
                OdinValidationUtils.AssertIsTrue(DealerEncryptedShard == null && !string.IsNullOrEmpty(DepositContent) &&
                                                 DepositKeyHeader?.IsSealed() == true,
                    $"A gated shard for {Player.OdinId} must be sealed to them");
                DepositKeyHeader.AssertValidSealed();
                break;
            default:
                throw new OdinClientException($"Unsupported player type {Player.Type}", OdinClientErrorCode.ArgumentError);
        }
    }
}
