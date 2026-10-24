using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core.Identity;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Query;
using Odin.Services.Drives.FileSystem.Standard;
using Odin.Services.Drives.Management;
using Odin.Services.LastSeen;
using Odin.Services.Peer.Encryption;
using Odin.Services.Security.ZeroKnowledge;


namespace Odin.Services.Security.PasswordRecovery.Shamir;

public abstract class ShamirBaseService<TLogger>(
    ILogger<TLogger> logger,
    StandardFileSystem fileSystem,
    IDriveManager driveManager,
    ILastSeenService lastSeenService)
{
    public readonly ILogger<TLogger> Logger = logger;
    public readonly StandardFileSystem FileSystem = fileSystem;

    public const string DealerShardConfigUid = "88be2b93-a5af-4884-adff-c73b2c9b04d4";

    /// <summary>
    /// Gets the shard a player has stored for a dealer.  i.e. this is the info that will be returned
    /// when a dealer requests to reset their password
    /// </summary>
    /// <remarks>
    /// A gated friend's shard is a deposit (docs/zero-knowledge-recovery-plan.md, "Building the friends
    /// pair"): this server can't open it, so the shard comes back without its piece, built from the
    /// file's plain metadata (the shard id, the recovery email hash, when it arrived).
    /// </remarks>
    public async Task<StoredShard> GetShardStoredForDealer(Guid shardId, IOdinContext odinContext)
    {
        var driveId = WellKnownAppDrives.ShardRecoveryDrive.Alias;

        var options = new ResultOptions
        {
            MaxRecords = 1,
            IncludeHeaderContent = true,
            ExcludePreviewThumbnail = true,
            ExcludeServerMetaData = false,
            IncludeTransferHistory = false
        };

        var byPassAclCheckContext = OdinContextUpgrades.UpgradeToByPassAclCheck(
            WellKnownAppDrives.ShardRecoveryDrive,
            DrivePermission.Read,
            odinContext);

        var file = await FileSystem.Query.GetFileByClientUniqueId(driveId, shardId, options, byPassAclCheckContext);

        if (file == null)
        {
            return new StoredShard(null, null, null);
        }

        OdinId? sender = string.IsNullOrEmpty(file.FileMetadata.SenderOdinId) ? null : (OdinId)file.FileMetadata.SenderOdinId;
        if (!file.FileMetadata.IsEncrypted)
        {
            return new StoredShard(PlayerEncryptedShard.Deserialize(file.FileMetadata.AppData.Content), sender, null);
        }

        var shard = new PlayerEncryptedShard
        {
            Id = shardId,
            RecoveryEmailHash = file.FileMetadata.AppData.GroupId.GetValueOrDefault(),
            Player = new ShamiraPlayer { OdinId = odinContext.Tenant, Type = PlayerType.Delegate },
            Created = file.FileMetadata.Created
        };

        return new StoredShard(shard, sender, file.SharedSecretEncryptedKeyHeader);
    }

    /// <summary>
    /// Whether <paramref name="stored"/> can still be opened by this identity's browser: a gated shard is
    /// sealed to the Shard Recovery drive's write-only key, which a reset with today's master key
    /// replaces (the drive is converted again).  An unencrypted gated shard is from a deal the server
    /// made before recovery in the browser, and no longer counts.
    /// </summary>
    public async Task<bool> CanReleaseAsync(StoredShard stored)
    {
        if (stored.Shard.Player.Type == PlayerType.Automatic)
        {
            return true;
        }

        if (stored.DepositKeyHeader == null)
        {
            return false;
        }

        var drive = await driveManager.GetDriveAsync(WellKnownAppDrives.ShardRecoveryDrive.Alias);
        return drive?.WriteOnlyKeyPair != null &&
               ZkSeal.ReadKeyId(stored.DepositKeyHeader.EncryptedAesKey) == ZkSeal.KeyId(drive.WriteOnlyKeyPair.PublicKeyJwk());
    }

    public async Task<DealerShardPackage> GetDealerShardPackage(IOdinContext odinContext)
    {
        var driveId = WellKnownAppDrives.ShardRecoveryDrive.Alias;
        var shardDrive = await driveManager.GetDriveAsync(driveId);

        if (null == shardDrive)
        {
            Logger.LogDebug(
                "Shard drive not yet configured (Tenant probably needs to upgrade).  So GetDealerShardPackage will return null.");
            return null;
        }

        var uid = Guid.Parse(DealerShardConfigUid);
        var options = new ResultOptions
        {
            MaxRecords = 1,
            IncludeHeaderContent = true,
            ExcludePreviewThumbnail = true,
            ExcludeServerMetaData = true,
            IncludeTransferHistory = false
        };

        var existingFile = await FileSystem.Query.GetFileByClientUniqueId(driveId, uid, options, odinContext);

        if (null == existingFile)
        {
            return null;
        }

        var json = existingFile.FileMetadata.AppData.Content;
        var package = DealerShardPackage.Deserialize(json);
        package.Updated = existingFile.FileMetadata.Updated;

        return package;
    }

    public async Task<ShardTrustLevel> GetTrustLevel(IOdinContext odinContext)
    {
        var lastSeen = await lastSeenService.GetLastSeenAsync(odinContext.Tenant);
        var trustLevel = ShardTrustLevel.Critical;
        if (lastSeen.HasValue)
        {
            var now = DateTime.UtcNow;
            var elapsed = now - lastSeen.Value.ToDateTime();

            if (elapsed < TimeSpan.FromDays(14))
            {
                trustLevel = ShardTrustLevel.High;
            }
            else if (elapsed < TimeSpan.FromDays(30))
            {
                trustLevel = ShardTrustLevel.Low;
            }
            else if (elapsed < TimeSpan.FromDays(90))
            {
                trustLevel = ShardTrustLevel.Medium;
            }
        }

        return trustLevel;
    }
}

/// <summary>
/// A dealer's shard as its holder stores it, and who dealt it.  <see cref="DepositKeyHeader"/> is set for
/// a gated friend's shard, a deposit: the key header sealed to the holder's write-only key.
/// </summary>
public sealed record StoredShard(PlayerEncryptedShard Shard, OdinId? Sender, EncryptedKeyHeader DepositKeyHeader);
