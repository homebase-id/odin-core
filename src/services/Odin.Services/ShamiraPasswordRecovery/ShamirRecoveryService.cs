using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MediatR;
using Microsoft.Extensions.Logging;
using Odin.Core;
using Odin.Core.Cryptography.Crypto;
using Odin.Core.Exceptions;
using Odin.Core.Identity;
using Odin.Core.Serialization;
using Odin.Core.Storage;
using Odin.Core.Storage.Database.Identity;
using Odin.Core.Storage.Database.Identity.Cache;
using Odin.Core.Storage.Database.Identity.Table;
using Odin.Core.Time;
using Odin.Services.AppNotifications.ClientNotifications;
using Odin.Services.Base;
using Odin.Services.Configuration;
using Odin.Services.Drives;
using Odin.Services.Drives.FileSystem.Standard;
using Odin.Services.Email;
using Odin.Services.JobManagement;
using Odin.Services.Membership.Connections;
using Odin.Services.ShamiraPasswordRecovery.ShardCollection;
using Odin.Services.ShamiraPasswordRecovery.ShardRequestApproval;

namespace Odin.Services.ShamiraPasswordRecovery;

/// <summary>
/// Handles scenarios where the Owner has lost their master password
/// and need to get shards from their peer network
/// </summary>
public class ShamirRecoveryService
{
    private readonly ShamirConfigurationService _configurationService;
    private readonly TenantContext _tenantContext;
    private readonly TableKeyValueCached _keyValueTable;
    private readonly TableNonce _nonceTable;
    private readonly OdinConfiguration _configuration;
    private readonly IJobManager _jobManager;
    private readonly IOdinHttpClientFactory _odinHttpClientFactory;
    private readonly CircleNetworkService _circleNetworkService;
    private readonly IdentityDatabase _db;
    private readonly IMediator _mediator;
    private readonly ILogger<ShamirRecoveryService> _logger;
    private readonly PlayerShardCollector _playerShardCollector;
    private readonly ShardRequestApprovalCollector _approvalCollector;

    /// <summary>
    /// Handles scenarios where the Owner has lost their master password and need to get shards from their peer network
    /// </summary>
    public ShamirRecoveryService(ShamirConfigurationService configurationService,
        TenantContext tenantContext,
        TableKeyValueCached keyValueTable,
        TableNonce nonceTable,
        OdinConfiguration configuration,
        IJobManager jobManager,
        IOdinHttpClientFactory odinHttpClientFactory,
        StandardFileSystem fileSystem,
        IdentityDatabase db,
        IMediator mediator,
        CircleNetworkService circleNetworkService,
        ILogger<ShamirRecoveryService> logger)
    {
        _configurationService = configurationService;
        _tenantContext = tenantContext;
        _keyValueTable = keyValueTable;
        _nonceTable = nonceTable;
        _configuration = configuration;
        _jobManager = jobManager;
        _odinHttpClientFactory = odinHttpClientFactory;
        _db = db;
        _mediator = mediator;
        _logger = logger;
        _circleNetworkService = circleNetworkService;

        _playerShardCollector = new PlayerShardCollector(fileSystem);
        _approvalCollector = new ShardRequestApprovalCollector(fileSystem, mediator);
    }

    private static readonly Guid ShamirStatusStorageId = Guid.Parse("d2180696-2d18-41e3-8699-c90c0d3aa710");
    private const string ContextKey = "aa575e4a-ffc6-44a1-8ea6-077ee1171a9d";
    private static readonly SingleKeyValueStorage Storage = TenantSystemStorage.CreateSingleKeyValueStorage(Guid.Parse(ContextKey));

    /// <summary>
    /// Sets the identity into recovery mode so peer shard holders can give the parts
    /// </summary>
    public async Task InitiateRecoveryModeEntry(IOdinContext odinContext)
    {
        _logger.LogDebug("Initiating recovery mode");

        var players = await _configurationService.GetPlayers(odinContext);

        await UpdateStatus(new ShamirRecoveryStatusRecord
        {
            Updated = UnixTimeUtc.Now(),
            State = ShamirRecoveryState.AwaitingOwnerEmailVerificationToEnterRecoveryMode
        });

        await EnqueueVerificationEmail(players, RecoveryEmailType.EnterRecoveryMode);
    }

    public async Task InitiateRecoveryModeExit(IOdinContext odinContext)
    {
        await EnqueueVerificationEmail([], RecoveryEmailType.ExitRecoveryMode);

        await UpdateStatus(new ShamirRecoveryStatusRecord
        {
            Updated = UnixTimeUtc.Now(),
            State = ShamirRecoveryState.AwaitingOwnerEmailVerificationToExitRecoveryMode
        });
    }

    public async Task<ShamirRecoveryStatusRedacted> GetStatus(IOdinContext odinContext)
    {
        var status = await GetStatusRecordInternal();
        var maskedEmail = EmailMasker.Mask(_tenantContext.Email);
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

    /// <summary>
    /// Puts this identity into recovery mode
    /// </summary>
    public async Task EnterRecoveryMode(Guid nonceId, string token, IOdinContext odinContext)
    {
        var record = await _nonceTable.PopAsync(nonceId);

        if (record == null)
        {
            throw new OdinClientException("Invalid id");
        }

        await UpdateStatus(new ShamirRecoveryStatusRecord()
        {
            Updated = UnixTimeUtc.Now(),
            State = ShamirRecoveryState.AwaitingSufficientDelegateConfirmation
        });

        //TODO: move this to an outbox call

        // notify all players this identity needs their password shards
        var nc = OdinContextUpgrades.UpgradeToByPassAclCheck(SystemDriveConstants.ShardRecoveryDrive, DrivePermission.Read, odinContext);
        var package = await _configurationService.GetDealerShardPackage(nc);
        foreach (var envelope in package.Envelopes)
        {
            var client = await CreateClientAsync(envelope.Player.OdinId);
            var response = await client.RequestShard(new RetrieveShardRequest
            {
                ShardId = envelope.ShardId
            });

            if (response.IsSuccessStatusCode)
            {
                var retrieveShardResult = response.Content;
                if (retrieveShardResult.ResultType == RetrieveShardResultType.Complete)
                {
                    await HandleAcceptRecoveryShard(retrieveShardResult, odinContext);
                }

                if (retrieveShardResult.ResultType == RetrieveShardResultType.WaitingForPlayer)
                {
                    //hmm, do we tell the user anything or just go into waiting mode?
                }
            }
        }
    }

    public async Task ExitRecoveryMode(Guid nonceId, string token, IOdinContext odinContext)
    {
        var record = await _nonceTable.PopAsync(nonceId);

        if (record == null)
        {
            throw new OdinClientException("Invalid id");
        }

        await Storage.DeleteAsync(_keyValueTable, ShamirStatusStorageId);
    }

    public async Task<RetrieveShardResult> HandleRetrieveShardRequest(RetrieveShardRequest request, IOdinContext odinContext)
    {
        var requester = odinContext.Caller.OdinId.GetValueOrDefault();

        // look up the shard info
        var (dealer, shard, _) = await _configurationService.GetShardStoredForDealer(request.ShardId, odinContext);

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

        if (shard.Player.Type == PlayerType.Delegate)
        {
            ShardApprovalRequest r = new ShardApprovalRequest
            {
                Id = shard.Id,
                Player = shard.Player.OdinId,
                Created = UnixTimeUtc.Now()
            };

            await _approvalCollector.SaveRequest(r, odinContext);

            shard.DealerEncryptedShard.Wipe();
            return new RetrieveShardResult
            {
                ResultType = RetrieveShardResultType.WaitingForPlayer,
                Shard = shard,
            };
        }

        throw new OdinSystemException($"How did we get here?  The player type is not delegate or " +
                                      $"automatic. It is {shard.Player.Type} for Player {shard.Player.OdinId} and " +
                                      $"id {shard.Id}");
    }

    /// <summary>
    /// A player has sent a shard to the dealer
    /// </summary>
    public async Task HandleAcceptRecoveryShard(RetrieveShardResult result, IOdinContext odinContext)
    {
        if (!await IsInRecoveryMode())
        {
            throw new OdinClientException("Not in recovery mode");
        }

        if (result.ResultType != RetrieveShardResultType.Complete)
        {
            throw new OdinClientException("Invalid result type");
        }

        odinContext = OdinContextUpgrades.UpgradeToByPassAclCheck(
            SystemDriveConstants.ShardRecoveryDrive,
            DrivePermission.Read,
            odinContext);

        await using var tx = await _db.BeginStackedTransactionAsync();

        await _playerShardCollector.SavePlayerShard(result.Shard, odinContext);
        var collectedShards = await _playerShardCollector.GetCollectShards(odinContext);

        var player = odinContext.Caller.OdinId.GetValueOrDefault();
        var package = await _configurationService.GetDealerShardPackage(odinContext);
        if (collectedShards.Count >= package.MinMatchingShards)
        {
            var (finalRecoveryKey, finalInfo) = await PrepareFinalKeys(collectedShards, package, odinContext);
            await EnqueueFinalizeRecoveryEmail(await MakeNonce(OdinSystemSerializer.Serialize(finalInfo)), finalRecoveryKey);

            await UpdateStatus(new ShamirRecoveryStatusRecord
            {
                Updated = UnixTimeUtc.Now(),
                State = ShamirRecoveryState.AwaitingOwnerFinalization,
            });

            await _mediator.Publish(new ShamirPasswordRecoverySufficientShardsCollectedNotification()
            {
                OdinContext = odinContext
            });
        }
        else
        {
            // collect the shards, so I can piece together my password
            int remainingRequired = package.MinMatchingShards - collectedShards.Count;
            await _mediator.Publish(new ShamirPasswordRecoveryShardCollectedNotification
            {
                OdinContext = odinContext,
                Sender = player,
                AdditionalMessage = $"You need {remainingRequired} more shards to recover your identity."
            });
        }

        tx.Commit();
    }

    public async Task<FinalRecoveryResult> FinalizeRecovery(Guid nonceId, Guid finalKey, IOdinContext odinContext)
    {
        // get nonce
        var record = await _nonceTable.PopAsync(nonceId);
        if (record == null)
        {
            throw new OdinClientException("Invalid Id");
        }

        // decrypt the nonce
        var recoveryInfo = OdinSystemSerializer.DeserializeOrThrow<FinalRecoveryInfo>(record.data);
        var finalKeyBytes = finalKey.ToByteArray().ToSensitiveByteArray();
        var recoveryTextBytes = AesCbc.Decrypt(recoveryInfo.Cipher, finalKeyBytes, recoveryInfo.Iv);
        var recoveryText = recoveryTextBytes.ToStringFromUtf8Bytes();

        // now that we have the recovery key, clean up all shards and reset status
        await ExitRecoveryModeInternal(odinContext);

        return new FinalRecoveryResult()
        {
            RecoveryText = recoveryText
        };
    }

    public async Task<List<ShardApprovalRequest>> GetShardRequestList(IOdinContext odinContext)
    {
        odinContext.Caller.AssertCallerIsOwner();
        var requests = await _approvalCollector.GetRequests(odinContext);
        return requests.ToList();
    }

    public async Task ApproveShardRequest(Guid shardId, OdinId dealerId, IOdinContext odinContext)
    {
        odinContext.Caller.AssertCallerIsOwner();

        var tx = await _db.BeginStackedTransactionAsync();
        var (dealer, shard, _) = await _configurationService.GetShardStoredForDealer(shardId, odinContext);

        var client = await CreateClientAsyncWithToken(dealer, null, odinContext);
        await client.SendPlayerShard(new RetrieveShardResult
        {
            ResultType = RetrieveShardResultType.Complete,
            Shard = shard
        });

        await _approvalCollector.DeleteRequest(shardId, odinContext);
        tx.Commit();
    }

    public async Task RejectShardRequest(Guid shardId, OdinId dealerId, IOdinContext odinContext)
    {
        odinContext.Caller.AssertCallerIsOwner();
        await _approvalCollector.DeleteRequest(shardId, odinContext);
    }

    private async Task<(Guid finalRecoveryKey, FinalRecoveryInfo finalInfo)> PrepareFinalKeys(
        List<PlayerEncryptedShard> collectedShards,
        DealerShardPackage package,
        IOdinContext odinContext)
    {
        var decryptedShards = new List<ShamirSecretSharing.ShamirShard>();
        foreach (var shard in collectedShards)
        {
            var envelope = package.Envelopes.FirstOrDefault(e => e.Player.OdinId == shard.Player.OdinId);
            if (null == envelope?.EncryptionKey)
            {
                _logger.LogWarning("Missing key for player: [{player}] for shardId: [{sid}]; continuing.",
                    shard.Player.OdinId,
                    shard.Id);

                continue;
            }

            var decryptedShard = AesCbc.Decrypt(shard.DealerEncryptedShard, envelope.EncryptionKey, envelope.EncryptionIv);
            var shamirShard = new ShamirSecretSharing.ShamirShard(shard.Index, decryptedShard);
            decryptedShards.Add(shamirShard);
        }

        // decrypt all the shards
        var distributionKey = ShamirSecretSharing.ReconstructShamirSecret(decryptedShards.OrderBy(s => s.Index).ToList());

        // put the recovery text in the nonce
        var recoveryText = await _configurationService.DecryptRecoveryKey(distributionKey.ToSensitiveByteArray(), odinContext);
        var finalRecoveryKey = ByteArrayUtil.GetRandomCryptoGuid();

        var (iv, cipher) = AesCbc.Encrypt(recoveryText.ToUtf8ByteArray(), finalRecoveryKey.ToByteArray().ToSensitiveByteArray());
        var finalInfo = new FinalRecoveryInfo()
        {
            Iv = iv,
            Cipher = cipher
        };

        return (finalRecoveryKey, finalInfo);
    }

    private async Task<bool> IsInRecoveryMode()
    {
        var status = await GetStatusRecordInternal();
        if (status == null)
        {
            return false;
        }

        return status.State == ShamirRecoveryState.AwaitingSufficientDelegateConfirmation ||
               status.State == ShamirRecoveryState.AwaitingOwnerEmailVerificationToExitRecoveryMode ||
               status.State == ShamirRecoveryState.AwaitingOwnerFinalization;
    }

    private async Task<ShamirRecoveryStatusRecord> GetStatusRecordInternal()
    {
        var record = await Storage.GetAsync<ShamirRecoveryStatusRecord>(_keyValueTable, ShamirStatusStorageId);
        return record;
    }

    private async Task UpdateStatus(ShamirRecoveryStatusRecord statusRecord)
    {
        await Storage.UpsertAsync(_keyValueTable, ShamirStatusStorageId, statusRecord);
    }

    private async Task EnqueueVerificationEmail(List<ShamiraPlayer> players, RecoveryEmailType emailType)
    {
        if (!_configuration.Mailgun.Enabled)
        {
#if !DEBUG
            throw new OdinSystemException("Cannot enter into recovery mode when email is disabled");
#endif
        }

        _logger.LogDebug("Enqueueing verification email");
        var job = _jobManager.NewJob<SendRecoveryModeVerificationEmailJob>();
        job.Data = new SendRecoveryModeVerificationEmailJobData()
        {
            Domain = _tenantContext.HostOdinId,
            Email = _tenantContext.Email,
            Players = players,
            NonceId = await MakeNonce(),
            EmailType = emailType,
            Path = emailType == RecoveryEmailType.EnterRecoveryMode ? "verify-enter" : "verify-exit"
        };

#if DEBUG
        var link = job.CreateLink();
        _logger.LogInformation("\n\n\n{link}\n\n\n", link);
#endif

        if (_configuration.Mailgun.Enabled)
        {
            await _jobManager.ScheduleJobAsync(job, new JobSchedule
            {
                RunAt = DateTimeOffset.Now.AddSeconds(1),
                MaxAttempts = 20,
                RetryDelay = TimeSpan.FromMinutes(1),
                OnSuccessDeleteAfter = TimeSpan.FromMinutes(1),
                OnFailureDeleteAfter = TimeSpan.FromMinutes(1),
            });
        }
    }

    private async Task EnqueueFinalizeRecoveryEmail(Guid nonceId, Guid finalRecoveryKey)
    {
        var tenant = _tenantContext.HostOdinId;
        var link = EmailLinkHelper.BuildResetUrl($"https://{tenant}/owner/shamir-account-recovery", nonceId,
            finalRecoveryKey.ToString());

#if DEBUG
        _logger.LogInformation("\n\n\n{link}\n\n\n", link);
#endif

        if (!_configuration.Mailgun.Enabled)
        {
#if !DEBUG
            throw new OdinSystemException("Cannot enter into recovery mode when email is disabled");
#else
            return;
#endif
        }

        var job = _jobManager.NewJob<SendEmailJob>();
        job.Data = new SendEmailJobData()
        {
            Envelope = new Envelope
            {
                To = [new NameAndEmailAddress { Email = _tenantContext.Email }],
                Subject = "We have assembled your recovery key!",
                TextMessage = RecoveryEmails.FinalizeRecoveryUsingRecoveryKeyText(tenant, link),
                HtmlMessage = RecoveryEmails.FinalizeRecoveryUsingRecoveryKeyHtml(tenant, link)
            },
        };

        await _jobManager.ScheduleJobAsync(job, new JobSchedule
        {
            RunAt = DateTimeOffset.Now.AddSeconds(1),
            MaxAttempts = 20,
            RetryDelay = TimeSpan.FromMinutes(1),
            OnSuccessDeleteAfter = TimeSpan.FromMinutes(1),
            OnFailureDeleteAfter = TimeSpan.FromMinutes(1),
        });
    }

    private async Task<Guid> MakeNonce(string data = "")
    {
        var nonceId = Guid.NewGuid();
        var r = new NonceRecord()
        {
            id = nonceId,
            expiration = UnixTimeUtc.Now().AddHours(1), // 1 hour expiration
            data = data
        };

        await _nonceTable.InsertAsync(r);
        return nonceId;
    }

    private Task<IPeerPasswordRecoveryHttpClient> CreateClientAsync(OdinId odinId)
    {
        var httpClient = _odinHttpClientFactory.CreateClient<IPeerPasswordRecoveryHttpClient>(odinId);
        return Task.FromResult(httpClient);
    }

    private async Task<IPeerPasswordRecoveryHttpClient> CreateClientAsyncWithToken(OdinId odinId,
        FileSystemType? fileSystemType,
        IOdinContext odinContext)
    {
        //Note here we override the permission check because we have either UseTransitWrite or UseTransitRead
        var icr = await _circleNetworkService.GetIcrAsync(odinId, odinContext, overrideHack: true, tryUpgradeEncryption: true);
        var authToken = icr.IsConnected() ? icr.CreateClientAuthToken(odinContext.PermissionsContext.GetIcrKey()) : null;
        var httpClient =
            _odinHttpClientFactory.CreateClientUsingAccessToken<IPeerPasswordRecoveryHttpClient>(odinId, authToken, fileSystemType);
        return httpClient;
    }

    private async Task ExitRecoveryModeInternal(IOdinContext odinContext)
    {
        await Storage.DeleteAsync(_keyValueTable, ShamirStatusStorageId);
        await _playerShardCollector.DeleteCollectedShards(odinContext);
    }
}