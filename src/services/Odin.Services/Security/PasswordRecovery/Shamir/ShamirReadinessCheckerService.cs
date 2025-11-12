using System;
using System.Collections.Generic;
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
            IsValid = false,
            TrustLevel = await GetTrustLevel(odinContext)
        };
    }

    /// <summary>
    /// Checks a remote identity for its ability to hold a shard
    /// </summary>
    public async Task<RemotePlayerReadinessResult> VerifyRemotePlayerReadiness(OdinId odinId, IOdinContext odinContext)
    {
        var icr = await circleNetworkService.GetIcrAsync(odinId, odinContext);

        if (!icr.IsConfirmedConnection())
        {
            return new RemotePlayerReadinessResult()
            {
                IsValid = false,
                TrustLevel = ShardTrustLevel.Critical
            };
        }

        try
        {
            var client = CreateClientAsync(odinId, odinContext);
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
            var result = await VerifyRemotePlayerShard(envelope.Player.OdinId, envelope.ShardId, odinContext);
            results.Add(envelope.Player.OdinId, result);
        }

        return new RemoteShardVerificationResult()
        {
            Players = results
        };
    }

    public async Task<ShardVerificationResult> VerifyRemotePlayerShard(OdinId player, Guid shardId, IOdinContext odinContext)
    {
        //todo: change to generic file system call
        try
        {
            var client = CreateClientAsync(player, odinContext);
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

        return new ShardVerificationResult
        {
            RemoteServerError = true,
            IsValid = false,
            Created = UnixTimeUtc.Now(),
            TrustLevel = ShardTrustLevel.Critical
        };
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
            var shardDrive = await _driveManager.GetDriveAsync(SystemDriveConstants.ShardRecoveryDrive.Alias);

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

    private IPeerPasswordRecoveryHttpClient CreateClientAsync(OdinId odinId, IOdinContext odinContext)
    {
        // var icr = await circleNetworkService.GetIcrAsync(odinId, odinContext);
        // var authToken = icr.IsConnected() ? icr.CreateClientAuthToken(odinContext.PermissionsContext.GetIcrKey()) : null;
        // var httpClient = odinHttpClientFactory.CreateClientUsingAccessToken<IPeerPasswordRecoveryHttpClient>(
        //     odinId, authToken, FileSystemType.Standard);
        // return (icr, httpClient);

        var httpClient = odinHttpClientFactory.CreateClient<IPeerPasswordRecoveryHttpClient>(odinId, FileSystemType.Standard);
        return httpClient;
    }
}