using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core.Exceptions;
using Odin.Core.Identity;
using Odin.Core.Storage;
using Odin.Core.Time;
using Odin.Services.Base;
using Odin.Services.Configuration.VersionUpgrade;
using Odin.Services.Drives;
using Odin.Services.Drives.FileSystem.Standard;
using Odin.Services.Drives.Management;
using Odin.Services.LastSeen;
using Odin.Services.Membership.Connections;
using Refit;

namespace Odin.Services.Security.PasswordRecovery.Shamir;

public class ShamirReadinessCheckerService(
    ILogger<ShamirReadinessCheckerService> logger,
    IOdinHttpClientFactory odinHttpClientFactory,
    StandardFileSystem fileSystem,
    IDriveManager driveManager,
    ILastSeenService lastSeenService,
    CircleNetworkService circleNetworkService,
    VersionUpgradeScheduler versionUpgradeScheduler)
    : ShamirBaseService<ShamirReadinessCheckerService>(logger, fileSystem, driveManager, lastSeenService)
{
    private readonly ILogger<ShamirReadinessCheckerService> _logger = logger;
    private readonly IDriveManager _driveManager = driveManager;

    public async Task<RemotePlayerReadinessResult> VerifyReadiness(IOdinContext odinContext)
    {
        var (requiresUpgrade, _, _) = await versionUpgradeScheduler.RequiresUpgradeAsync();
        var isValid = !requiresUpgrade;
        //&& !isConfirmedConnection
        return new RemotePlayerReadinessResult()
        {
            IsValid = isValid,
            TrustLevel = await GetTrustLevel(odinContext)
        };
    }

    /// <summary>
    /// Checks a remote identity for its ability to hold a shard
    /// </summary>
    public async Task<RemotePlayerReadinessResult> VerifyRemotePlayerReadiness(OdinId odinId, IOdinContext odinContext)
    {
        var icr = await circleNetworkService.GetIcrAsync(odinId, odinContext);

        if (!icr.IsReviewed())
        {
            return new RemotePlayerReadinessResult()
            {
                IsValid = false,
                TrustLevel = ShardTrustLevel.Critical
            };
        }

        try
        {
            var client = await CreateClientAsync(odinId, odinContext);
            var response = await client.VerifyReadiness();

            if (response.IsSuccessStatusCode)
            {
                var result = response.Content;
                return result;
            }

            _logger.LogDebug("Shard verification call failed for identity: {identity}.  Http Status " +
                             "code: {code}", odinId, response.StatusCode);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Failed during shard verification for identity: {identity}", odinContext);
        }

        return new RemotePlayerReadinessResult()
        {
            IsValid = false,
            TrustLevel = ShardTrustLevel.Critical
        };
    }

    /// <summary>
    /// Verifies shards held by players
    /// </summary>
    public async Task<RemoteShardVerificationResult> VerifyRemotePlayerShards(IOdinContext odinContext)
    {
        // get the preconfigured package
        var package = await this.GetDealerShardPackage(odinContext);

        if (package == null)
        {
            _logger.LogDebug("Sharding for dealer {d} not configured.", odinContext.Caller);
            throw new OdinClientException("Sharding not configured");
        }

        var results = new Dictionary<string, ShardVerificationResult>();
        foreach (var envelope in package.Envelopes)
        {
            var result = await VerifyPlayerShardAsync(envelope.Player, envelope.ShardId, odinContext);
            results.Add(envelope.Player.OdinId, result);
        }

        return new RemoteShardVerificationResult()
        {
            Players = results
        };
    }

    /// <summary>
    /// Verifies the shard <paramref name="player"/> holds for this dealer; see <see cref="VerifyPlayerShardAsync"/>.
    /// </summary>
    public async Task<ShardVerificationResult> VerifyRemotePlayerShard(OdinId player, Guid shardId, IOdinContext odinContext)
    {
        var package = await this.GetDealerShardPackage(odinContext);
        var envelope = package?.Envelopes.FirstOrDefault(e => e.Player.OdinId == player && e.ShardId == shardId);
        if (null == envelope)
        {
            // not a shard this dealer configured, so not one recovery can use
            return NotUsable(remoteServerError: false);
        }

        return await VerifyPlayerShardAsync(envelope.Player, shardId, odinContext);
    }

    /// <summary>
    /// A shard counts only if the player can deliver it during recovery. A delegate delivers by
    /// writing to this identity, which needs a confirmed connection; holding the shard is not
    /// enough (#1885). Automated players are never connections and reply in-band.
    /// </summary>
    private async Task<ShardVerificationResult> VerifyPlayerShardAsync(ShamiraPlayer player, Guid shardId, IOdinContext odinContext)
    {
        if (player.Type == PlayerType.Delegate && !await CanDeliverShardAsync(player.OdinId, odinContext))
        {
            _logger.LogDebug("Delegate {identity} is not connected, so cannot deliver its shard", player.OdinId);
            var notConnected = NotUsable(remoteServerError: false);
            notConnected.IsConnected = false;
            return notConnected;
        }

        return await VerifyRemoteShardAsync(player.OdinId, shardId, odinContext);
    }

    /// <summary>
    /// One rule for whether a delegate can hold and deliver a shard, used both when choosing
    /// delegates and when checking them later.
    /// </summary>
    private async Task<bool> CanDeliverShardAsync(OdinId odinId, IOdinContext odinContext)
    {
        var icr = await circleNetworkService.GetIcrAsync(odinId, odinContext);
        return icr.IsReviewed();
    }

    private static ShardVerificationResult NotUsable(bool remoteServerError) => new()
    {
        RemoteServerError = remoteServerError,
        IsValid = false,
        Created = UnixTimeUtc.Now(),
        TrustLevel = ShardTrustLevel.Critical
    };

    private async Task<ShardVerificationResult> VerifyRemoteShardAsync(OdinId player, Guid shardId, IOdinContext odinContext)
    {
        //todo: change to generic file system call
        try
        {
            var client = await CreateClientAsync(player, odinContext);
            var response = await client.VerifyShard(new VerifyShardRequest()
            {
                ShardId = shardId
            });

            if (response.IsSuccessStatusCode)
            {
                var result = response.Content;
                _logger.LogDebug("Shard verification call succeed for identity: {identity}.  Result " +
                                 "was: IsValid:{isValid}.  remoteServerError: {remoteError}", player,
                    result.IsValid,
                    result.RemoteServerError);

                return result;
            }

            _logger.LogDebug("Shard verification call failed for identity: {identity}.  Http Status code: {code}", player,
                response.StatusCode);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Failed during shard verification for identity: {identity}", player);
        }

        return NotUsable(remoteServerError: true);
    }

    /// <summary>
    /// Verifies the shard given to this identity from a dealer
    /// </summary>
    public async Task<ShardVerificationResult> VerifyDealerShard(
        Guid shardId,
        IOdinContext odinContext)
    {
        odinContext.Caller.AssertCallerIsAuthenticated();
        _logger.LogDebug("Verifying dealer shard {shardId}", shardId);
        try
        {
            var shardDrive = await _driveManager.GetDriveAsync(WellKnownAppDrives.ShardRecoveryDrive.Alias);

            if (null == shardDrive)
            {
                _logger.LogDebug("Could not perform shard verification; Sharding drive not " +
                                 "yet configured (Tenant probably needs to upgrade)");

                return new ShardVerificationResult
                {
                    RemoteServerError = true,
                    IsValid = false,
                    TrustLevel = ShardTrustLevel.Critical,
                    Created = UnixTimeUtc.Now()
                };
            }

            var (shard, sender) = await GetShardStoredForDealer(shardId, odinContext);
            var isValid = shard != null && sender == odinContext.Caller.OdinId.GetValueOrDefault();

            if (isValid)
            {
                var trustLevel = ShardTrustLevel.Critical; // default = worst case
                if (shard.Player.Type == PlayerType.Automatic)
                {
                    trustLevel = ShardTrustLevel.High;
                }
                else
                {
                    trustLevel = await GetTrustLevel(odinContext);
                }

                return new ShardVerificationResult
                {
                    RemoteServerError = false,
                    IsValid = true,
                    TrustLevel = trustLevel,
                    Created = shard?.Created ?? 0
                };
            }

            // not valid - add some logging to see what's up
            if (shard == null)
            {
                _logger.LogDebug("Dealer shard with id: {shardId} is null", shardId);
            }

            if (sender != odinContext.Caller.OdinId.GetValueOrDefault())
            {
                _logger.LogDebug("Dealer shard with id: {shardId} has mismatching caller and sender", shardId);
            }

            return new ShardVerificationResult
            {
                RemoteServerError = false,
                IsValid = false,
                TrustLevel = ShardTrustLevel.Critical,
                Created = shard?.Created ?? 0
            };
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Could not perform shard verification");
            // if anything fails, just tell the caller this
            // server is not capable of sharding right now
            return new ShardVerificationResult
            {
                RemoteServerError = true,
                IsValid = false,
                TrustLevel = ShardTrustLevel.Critical,
                Created = UnixTimeUtc.Now()
            };
        }
    }

    private async Task<IPeerPasswordRecoveryHttpClient> CreateClientAsync(OdinId odinId, IOdinContext odinContext)
    {
        // var icr = await circleNetworkService.GetIcrAsync(odinId, odinContext);
        // var authToken = icr.IsConnected() ? icr.CreateClientAuthToken(odinContext.PermissionsContext.GetIcrKey()) : null;
        // var httpClient = odinHttpClientFactory.CreateClientUsingAccessToken<IPeerPasswordRecoveryHttpClient>(
        //     odinId, authToken, FileSystemType.Standard);
        // return (icr, httpClient);

        var httpClient = await odinHttpClientFactory.CreateClientAsync<IPeerPasswordRecoveryHttpClient>(odinId, FileSystemType.Standard);
        return httpClient;
    }
}