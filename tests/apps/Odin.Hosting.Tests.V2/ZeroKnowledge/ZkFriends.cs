#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core;
using Odin.Core.Cryptography.Crypto;
using Odin.Core.Serialization;
using Odin.Hosting.Controllers.OwnerToken.Security;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Security;
using Odin.Hosting.Tests.V2.Api;
using Odin.Services.Apps.Builtin;
using Odin.Services.Drives;
using Odin.Services.Peer.Encryption;
using Odin.Services.Peer.Incoming.Drive.Query;
using Odin.Services.Security.PasswordRecovery.Shamir;
using Odin.Services.Security.PasswordRecovery.Shamir.ShardRequestApproval;
using Odin.Services.Security.ZeroKnowledge;
using Odin.Hosting.Tests._V2.ApiClient;
using Refit;
using Odin.Hosting.Tests;

namespace Odin.Hosting.Tests.V2.ZeroKnowledge;

/// <summary>
/// The browser's side of recovery with friends, in C#, as <see cref="ZkBrowser"/> is for the phrase
/// (docs/zero-knowledge-recovery-plan.md, "Building the friends pair"): it deals, it opens and seals a
/// friend's shard again, and it rebuilds the secret.  The odin-js code must do the same.
/// </summary>
internal static class ZkFriends
{
    /// <summary>The friends' reset key: hkdf-sha256 of the secret, no salt, 16 bytes.</summary>
    public static byte[] ResetKey(byte[] secret) => ZkBrowser.Hkdf16(secret, "homebase-shamir-reset-key");

    public sealed record Deal(ConfigureShardsRequest Request, byte[] Secret, ZkBrowser.KeyPair ShamirKeyPair);

    /// <summary>
    /// What the console's deal dialog makes: a random secret split into shards, each locked by its own
    /// shard key, a gated friend's sealed to them as a deposit, and the friend deal's copies.
    /// </summary>
    public static async Task<Deal> MakeDealAsync(OwnerSession dealer, byte[] dealerZkMasterKey,
        IReadOnlyList<(OwnerSession Player, PlayerType Type)> players, int minMatchingShards)
    {
        var secret = ByteArrayUtil.GetRndByteArray(16);
        var pieces = ShamirSecretSharing.GenerateShamirShares(players.Count, minMatchingShards, secret);

        var shards = new List<DealtShard>();
        for (var i = 0; i < players.Count; i++)
        {
            var (player, type) = players[i];
            var piece = pieces[i];
            var shardKey = ByteArrayUtil.GetRndByteArray(16);
            var (shardIv, lockedPiece) = AesCbc.Encrypt(piece.Shard, shardKey.ToSensitiveByteArray());
            var playerRecord = new ShamiraPlayer { OdinId = player.Identity, Type = type };
            var shardId = Guid.NewGuid();

            var shard = new DealtShard
            {
                Player = playerRecord, ShardId = shardId, Index = piece.Index,
                EncryptionKey = shardKey, EncryptionIv = shardIv
            };

            if (type == PlayerType.Automatic)
            {
                shards.Add(shard with { DealerEncryptedShard = lockedPiece });
                continue;
            }

            var holderKey = await GetShardRecoveryPublicKeyAsync(dealer, player);
            var (content, keyHeader) = MakeDeposit(new PlayerEncryptedShard
            {
                Id = shardId, Index = piece.Index, Player = playerRecord, DealerEncryptedShard = lockedPiece
            }, holderKey.PublicKeyJwk);
            shards.Add(shard with { DepositContent = content, DepositKeyHeader = keyHeader });
        }

        var shamirPair = ZkBrowser.MakeKeyPair();
        var request = new ConfigureShardsRequest
        {
            MinMatchingShards = minMatchingShards,
            Shards = shards,
            ShamirCopies = new ZkShamirCopiesRequest
            {
                ShamirPublicKeyJwk = shamirPair.PublicKeyJwk,
                ShamirKeyEncryptedShamirPrivateKey = ZkBrowser.Lock(shamirPair.PrivateKeyPkcs8, secret),
                ShamirKeyEncryptedZkMasterKey = ZkBrowser.Lock(dealerZkMasterKey, secret),
                ResetKey64 = ResetKey(secret).ToBase64()
            }
        };

        return new Deal(request, secret, shamirPair);
    }

    /// <summary>A deposit of <paramref name="shard"/>'s JSON: encrypted with a new file key, sealed to <paramref name="publicKeyJwk"/>.</summary>
    public static (string Content, EncryptedKeyHeader KeyHeader) MakeDeposit(PlayerEncryptedShard shard, string publicKeyJwk)
    {
        var (fileKey, keyHeader) = ZkBrowser.SealNewFileKey(publicKeyJwk);
        return (fileKey.EncryptDataAes(OdinSystemSerializer.Serialize(shard).ToUtf8ByteArray()).ToBase64(), keyHeader);
    }

    /// <summary>The holder's Shard Recovery write-only key, as the dealer's browser fetches it.</summary>
    public static async Task<DrivePublicKeyResponse> GetShardRecoveryPublicKeyAsync(OwnerSession dealer, OwnerSession holder)
    {
        var (client, ss) = dealer.NewAdminHttpClient();
        var response = await RefitCreator.RestServiceFor<IPeerAppDriveHttpClientApiV2>(client, ss)
            .GetDriveWriteOnlyPublicKey(holder.Identity.DomainName, "recovery", BuiltinDrives.ShardRecoveryDrive.DriveSlug.ToString());
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), response.Error?.Content);
        return response.Content!;
    }

    /// <summary>
    /// A gated friend approving in their console: their browser opens the deposit with their Shard
    /// Recovery drive's write-only private key and seals the piece again to the recovering browser's key.
    /// </summary>
    public static async Task<ApiResponse<HttpContent>> ApproveAsync(OwnerSession holder, byte[] holderZkMasterKey, ShardApprovalRequest request)
    {
        var piece = await OpenGatedShardAsync(holder, holderZkMasterKey, request.ShardId);
        return await holder.RefitFor<ITestSecurityContextOwnerClient>().ApproveShardRequest(new ApproveShardRequest
        {
            OdinId = request.Dealer,
            ShardId = request.ShardId,
            Index = piece.Index,
            SealedShard = ZkSeal.Seal(piece.DealerEncryptedShard, request.RecoveryPublicKeyJwk)
        });
    }

    /// <summary>The gated shard <paramref name="holder"/> keeps for a dealer, opened in their browser.</summary>
    public static async Task<PlayerEncryptedShard> OpenGatedShardAsync(OwnerSession holder, byte[] holderZkMasterKey, Guid shardId)
    {
        var drive = await ZkOwnerSteps.GetShardRecoveryDriveAsync(holder);
        var writeOnlyPrivateKey = ZkBrowser.OpenWriteOnlyPrivateKey(drive.ZkMasterKeyEncryptedStorageKey,
            drive.ZkStorageKeyEncryptedWriteOnlyPrivateKey, holderZkMasterKey);

        var file = await holder.Drives.Reader.GetFileHeaderByUniqueIdAsync(shardId, WellKnownAppDrives.ShardRecoveryDrive.Alias);
        Assert.That(file.StatusCode, Is.EqualTo(HttpStatusCode.OK), file.Error?.Content);
        var json = ZkBrowser.OpenDeposit(file.Content!, writeOnlyPrivateKey).ToStringFromUtf8Bytes();
        return OdinSystemSerializer.DeserializeOrThrow<PlayerEncryptedShard>(json);
    }

    public sealed record Recovered(byte[] Secret, byte[] ZkMasterKey, string? Password, ShamirRecoveryBundle Bundle);

    /// <summary>
    /// What the recovering browser does with the bundle: opens it with its throwaway key, rebuilds the
    /// secret as <c>PrepareFinalKeys</c> does, and opens the zero-knowledge master key and the password.
    /// </summary>
    public static Recovered Rebuild(ShamirRecoveryBundleResponse response, ZkBrowser.KeyPair recoveringBrowser)
    {
        var json = ZkBrowser.Unseal(response.SealedBundle64, recoveringBrowser.PrivateKeyPkcs8).ToStringFromUtf8Bytes();
        var bundle = OdinSystemSerializer.DeserializeOrThrow<ShamirRecoveryBundle>(json);

        var pieces = bundle.Shards.Select(shard =>
        {
            var envelope = bundle.Envelopes.Single(e => e.ShardId == shard.Id);
            var locked = envelope.Player.Type == PlayerType.Delegate
                ? ZkBrowser.Unseal(shard.DealerEncryptedShard, recoveringBrowser.PrivateKeyPkcs8)
                : shard.DealerEncryptedShard;
            return new ShamirSecretSharing.ShamirShard(shard.Index,
                AesCbc.Decrypt(locked, envelope.EncryptionKey.ToSensitiveByteArray(), envelope.EncryptionIv));
        }).OrderBy(p => p.Index).ToList();

        var secret = ShamirSecretSharing.ReconstructShamirSecret(pieces, 16);
        var zkMasterKey = ZkBrowser.Open(bundle.Copies.ShamirKeyEncryptedZkMasterKey, secret);
        string? password = null;
        if (bundle.Copies.ShamirPublicKeyEncryptedPassword != null)
        {
            var privateKey = ZkBrowser.Open(bundle.Copies.ShamirKeyEncryptedShamirPrivateKey, secret);
            password = ZkBrowser.Unseal(bundle.Copies.ShamirPublicKeyEncryptedPassword, privateKey).ToStringFromUtf8Bytes();
        }

        return new Recovered(secret, zkMasterKey, password, bundle);
    }
}
