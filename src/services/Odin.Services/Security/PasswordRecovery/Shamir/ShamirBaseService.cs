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
    public async Task<(PlayerEncryptedShard shard, OdinId? sender)> GetShardStoredForDealer(
        Guid shardId,
        IOdinContext odinContext)
    {
        var driveId = SystemDriveConstants.ShardRecoveryDrive.Alias;

        var options = new ResultOptions
        {
            MaxRecords = 1,
            IncludeHeaderContent = true,
            ExcludePreviewThumbnail = true,
            ExcludeServerMetaData = false,
            IncludeTransferHistory = false
        };

        var byPassAclCheckContext = OdinContextUpgrades.UpgradeToByPassAclCheck(
            SystemDriveConstants.ShardRecoveryDrive,
            DrivePermission.Read,
            odinContext);

        var file = await FileSystem.Query.GetFileByClientUniqueId(driveId, shardId, options, byPassAclCheckContext);

        if (file == null)
        {
            return (null, null);
        }

        var shard = PlayerEncryptedShard.Deserialize(file.FileMetadata.AppData.Content);
        OdinId? sender = string.IsNullOrEmpty(file.FileMetadata.SenderOdinId) ? null : (OdinId)file.FileMetadata.SenderOdinId;
        return (shard, sender);
    }

    public async Task<DealerShardPackage> GetDealerShardPackage(IOdinContext odinContext)
    {
        var driveId = SystemDriveConstants.ShardRecoveryDrive.Alias;
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