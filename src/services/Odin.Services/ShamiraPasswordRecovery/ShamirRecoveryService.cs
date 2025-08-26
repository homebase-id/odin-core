using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core;
using Odin.Core.Cryptography.Crypto;
using Odin.Core.Exceptions;
using Odin.Core.Storage;
using Odin.Core.Storage.Database.Identity.Table;
using Odin.Core.Time;
using Odin.Services.AppNotifications.Push;
using Odin.Services.Base;
using Odin.Services.Configuration;
using Odin.Services.JobManagement;
using Odin.Services.Peer.Outgoing.Drive;

namespace Odin.Services.ShamiraPasswordRecovery;

/// <summary>
/// Handles scenarios where the Owner has lost their master password and need to get shards from their peer network
/// </summary>
public class ShamirRecoveryService(
    ShamirConfigurationService configurationService,
    TenantContext tenantContext,
    TableKeyValue keyValueTable,
    TableNonce nonceTable,
    OdinConfiguration configuration,
    IJobManager jobManager,
    PushNotificationService pushNotificationService,
    ILogger<ShamirRecoveryService> logger)
{
    private static readonly Guid ShamirStatusStorageId = Guid.Parse("d2180696-2d18-41e3-8699-c90c0d3aa710");
    private const string ContextKey = "aa575e4a-ffc6-44a1-8ea6-077ee1171a9d";
    private static readonly SingleKeyValueStorage Storage = TenantSystemStorage.CreateSingleKeyValueStorage(Guid.Parse(ContextKey));

    /// <summary>
    /// Sets the identity into recovery mode so peer shard holders can give the parts
    /// </summary>
    public async Task InitiateRecoveryMode(IOdinContext odinContext)
    {
        logger.LogDebug("Initiating recovery mode");

        var players = await configurationService.GetPlayers(odinContext);
        await EnqueueEmail(players, RecoveryEmailType.EnterRecoveryMode);

        await UpdateStatus(new ShamirRecoveryStatusRecord
        {
            Updated = UnixTimeUtc.Now(),
            State = ShamirRecoveryState.AwaitingOwnerEmailVerificationToEnterRecoveryMode
        });
    }

    public async Task InitiateExitRecoveryMode(IOdinContext odinContext)
    {
        await EnqueueEmail([], RecoveryEmailType.ExitRecoveryMode);

        await UpdateStatus(new ShamirRecoveryStatusRecord
        {
            Updated = UnixTimeUtc.Now(),
            State = ShamirRecoveryState.AwaitingOwnerEmailVerificationToExitRecoveryMode
        });
    }

    public async Task<ShamirRecoveryStatusRedacted> GetStatus(IOdinContext odinContext)
    {
        var status = await GetStatusRecordInternal();
        var maskedEmail = EmailMasker.Mask(tenantContext.Email);
        if (null == status)
        {
            return new ShamirRecoveryStatusRedacted()
            {
                Email = maskedEmail,
                State = ShamirRecoveryState.None
            };
        }

        return new ShamirRecoveryStatusRedacted
        {
            Updated = status.Updated,
            State = status.State,
            Email = maskedEmail
        };
    }

    public async Task<bool> IsInRecoveryMode(IOdinContext odinContext)
    {
        var status = await GetStatusRecordInternal();
        if (status == null)
        {
            return false;
        }

        return status.State == ShamirRecoveryState.AwaitingSufficientDelegateConfirmation;
    }

    /// <summary>
    /// Puts this identity into recovery mode
    /// </summary>
    public async Task EnterRecoveryMode(Guid nonceId, string token, IOdinContext odinContext)
    {
        var record = await nonceTable.PopAsync(nonceId);

        if (record == null)
        {
            throw new OdinClientException("Invalid id");
        }

        // notify all players this identity needs their password shards


        await UpdateStatus(new ShamirRecoveryStatusRecord()
        {
            Updated = UnixTimeUtc.Now(),
            State = ShamirRecoveryState.AwaitingSufficientDelegateConfirmation
        });
    }

    public async Task ExitRecoveryMode(Guid nonceId, string token, IOdinContext odinContext)
    {
        var record = await nonceTable.PopAsync(nonceId);

        if (record == null)
        {
            throw new OdinClientException("Invalid id");
        }

        await Storage.DeleteAsync(keyValueTable, ShamirStatusStorageId);
    }

    public async Task<RetrieveShardResult> HandleRetrieveShardRequest(RetrieveShardRequest request, IOdinContext odinContext)
    {
        var requester = odinContext.Caller.OdinId.GetValueOrDefault();

        // look up the shard info
        var (dealer, shard, _) = await configurationService.GetDealerShard(request.ShardId, odinContext);

        if (dealer != requester)
        {
            throw new OdinSecurityException("invalid requester");
        }

        if (shard.Player.Type == PlayerType.Automatic)
        {
            // return it now
            return new RetrieveShardResult
            {
                ResultType = RetrieveShardResultType.Complete,
                Shard = shard
            };
        }

        // send app notification
        var options = new AppNotificationOptions
        {
            AppId = default,
            TypeId = default,
            TagId = default,
            Silent = false,
            PeerSubscriptionId = default,
            Recipients = [],
            UnEncryptedMessage = $"{requester} has requested your assistance in recovering their identity"
        };

        await pushNotificationService.EnqueueNotification(requester, options, odinContext);

        shard.DealerEncryptedShard.Wipe();
        return new RetrieveShardResult
        {
            ResultType = RetrieveShardResultType.WaitingForPlayer,
            Shard = shard,
        };
    }

    /// <summary>
    /// A player has sent a shard to the dealer
    /// </summary>
    public async Task HandleAcceptRecoveryShard(RetrieveShardResult result, IOdinContext odinContext)
    {
        var player = odinContext.Caller.OdinId.GetValueOrDefault();
        if (!await IsInRecoveryMode(odinContext))
        {
            throw new OdinClientException("Not in recovery mode");
        }

        if (result.ResultType != RetrieveShardResultType.Complete)
        {
            throw new OdinClientException("Invalid result type");
        }


        var status = await GetStatusRecordInternal();
        status.CollectedShards.Add(result.Shard);
        await UpdateStatus(status);

        var package = await configurationService.GetDealerShardPackage(odinContext);
        if (status.CollectedShards.Count >= package.MinMatchingShards)
        {
            var decryptedShards = new List<ShamirSecretSharing.ShamirShard>();
            foreach (var shard in status.CollectedShards)
            {
                var envelope = package.Envelopes.FirstOrDefault(e => e.Player.OdinId == shard.Player.OdinId);
                if (null == envelope?.EncryptionKey)
                {
                    logger.LogWarning("Missing key for player: [{player}] for shardId: [{sid}]; continuing.", shard.Player.OdinId,
                        shard.Id);
                    continue;
                }

                var decryptedShard = AesCbc.Decrypt(shard.DealerEncryptedShard, envelope.EncryptionKey, envelope.EncryptionIv);
                var shamirShard = new ShamirSecretSharing.ShamirShard(shard.Index, decryptedShard);
                decryptedShards.Add(shamirShard);
            }

            // decrypt all the shards
            var secret = ShamirSecretSharing.ReconstructShamirSecret(decryptedShards);
        }
        else
        {
            int remainingRequired = package.MinMatchingShards - status.CollectedShards.Count;
            // collect the shard, so I can piece together my password
            // send app notification
            var options = new AppNotificationOptions
            {
                AppId = default,
                TypeId = default,
                TagId = default,
                Silent = false,
                PeerSubscriptionId = default,
                Recipients = [],
                UnEncryptedMessage = $"{player} has sent a shard to assist in recovering your " +
                                     $"identity.  You need {remainingRequired} more shards to recover " +
                                     $"your identity"
            };

            await pushNotificationService.EnqueueNotification(player, options, odinContext);
        }
    }

    private async Task<ShamirRecoveryStatusRecord> GetStatusRecordInternal()
    {
        var record = await Storage.GetAsync<ShamirRecoveryStatusRecord>(keyValueTable, ShamirStatusStorageId);
        return record;
    }

    private async Task UpdateStatus(ShamirRecoveryStatusRecord statusRecord)
    {
        await Storage.UpsertAsync(keyValueTable, ShamirStatusStorageId, statusRecord);
    }

    private async Task EnqueueEmail(List<ShamiraPlayer> players, RecoveryEmailType emailType)
    {
        if (!configuration.Mailgun.Enabled)
        {
#if !DEBUG
            throw new OdinClientException("Cannot enter into recovery mode when email is disabled");
#endif
        }

        var nonceId = Guid.NewGuid();
        var r = new NonceRecord()
        {
            id = nonceId,
            expiration = UnixTimeUtc.Now().AddHours(1), // 1 hour expiration
            data = ""
        };

        await nonceTable.InsertAsync(r);

        var job = jobManager.NewJob<SendRecoveryModeVerificationEmailJob>();
        job.Data = new SendRecoveryModeVerificationEmailJobData()
        {
            Domain = tenantContext.HostOdinId,
            Email = tenantContext.Email,
            Players = players,
            NonceId = nonceId,
            EmailType = emailType,
            Path = emailType == RecoveryEmailType.EnterRecoveryMode ? "verify-enter" : "verify-exit"
        };

#if DEBUG
        var link = job.CreateLink();
        logger.LogInformation(link);
#endif

        await jobManager.ScheduleJobAsync(job, new JobSchedule
        {
            RunAt = DateTimeOffset.Now.AddSeconds(1),
            MaxAttempts = 20,
            RetryDelay = TimeSpan.FromMinutes(1),
            OnSuccessDeleteAfter = TimeSpan.FromMinutes(1),
            OnFailureDeleteAfter = TimeSpan.FromMinutes(1),
        });
    }
}