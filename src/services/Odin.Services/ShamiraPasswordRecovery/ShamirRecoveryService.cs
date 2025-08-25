using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core.Exceptions;
using Odin.Core.Identity;
using Odin.Core.Storage;
using Odin.Core.Storage.Database.Identity.Table;
using Odin.Core.Time;
using Odin.Services.Base;
using Odin.Services.Configuration;
using Odin.Services.JobManagement;

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

    private async Task<ShamirRecoveryStatusRecord> GetStatusRecordInternal()
    {
        var record = await Storage.GetAsync<ShamirRecoveryStatusRecord>(keyValueTable, ShamirStatusStorageId);
        return record;
    }

    private async Task UpdateStatus(ShamirRecoveryStatusRecord statusRecord)
    {
        await Storage.UpsertAsync(keyValueTable, ShamirStatusStorageId, statusRecord);
    }

    private async Task<Guid> EnqueueEmail(List<OdinId> players, RecoveryEmailType emailType)
    {
        if (!configuration.Mailgun.Enabled)
        {
#if DEBUG
            return Guid.Empty;
#else
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

        return nonceId;
    }
}