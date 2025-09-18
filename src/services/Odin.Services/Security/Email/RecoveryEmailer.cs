using System;
using System.Collections.Generic;
using System.Net.Mail;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core.Exceptions;
using Odin.Core.Serialization;
using Odin.Core.Storage.Database.Identity.Table;
using Odin.Core.Time;
using Odin.Services.Authentication.Owner;
using Odin.Services.Base;
using Odin.Services.Configuration;
using Odin.Services.Email;
using Odin.Services.JobManagement;
using Odin.Services.Security.PasswordRecovery.Shamir;

namespace Odin.Services.Security.Email;

public class RecoveryEmailer(
    OdinConfiguration configuration,
    ILogger<RecoveryEmailer> logger,
    TenantContext tenantContext,
    TableNonce nonceTable,
    IJobManager jobManager)
{
    /// <summary>
    /// Uses for integration testing so I can get the nonceId from the log during ShamirPasswordRecoveryTests
    /// </summary>
    public const string NoncePropertyName = "nonceId";

    public async Task<string> GetNonceDataOrFail(Guid nonceId)
    {
        var record = await nonceTable.PopAsync(nonceId);
        if (record == null)
        {
            throw new OdinClientException("Invalid Nonce");
        }

        return record.data;
    }

    public async Task EnqueueVerifyNewRecoveryEmailAddress(MailAddress email)
    {
        logger.LogDebug("Enqueueing verify new recovery email address");

        AssertEmailEnabled();

        var tenant = tenantContext.HostOdinId;
        var nonceId = await MakeNonce(email.Address);

        var link = BuildResetUrl($"https://{tenant}{OwnerApiPathConstants.SecurityRecoveryV1}/verify-email", nonceId, "");

#if DEBUG
        logger.LogInformation("\n\n\n{link}\n\n\n{nonceId}", link, nonceId);
#endif

        var job = jobManager.NewJob<SendEmailJob>();
        job.Data = new SendEmailJobData()
        {
            Envelope = new Envelope
            {
                To = [new NameAndEmailAddress { Name = email.DisplayName, Email = email.Address }],
                Subject = "Please verify your new recovery email address!",
                TextMessage = RecoveryEmails.VerifyNewRecoveryEmailText(tenant, link),
                HtmlMessage = RecoveryEmails.VerifyNewRecoveryEmailHtml(tenant, link)
            },
        };

        await jobManager.ScheduleJobAsync(job, new JobSchedule
        {
            RunAt = DateTimeOffset.Now.AddSeconds(1),
            MaxAttempts = 20,
            RetryDelay = TimeSpan.FromMinutes(1),
            OnSuccessDeleteAfter = TimeSpan.FromMinutes(1),
            OnFailureDeleteAfter = TimeSpan.FromMinutes(1),
        });

        if (configuration.Mailgun.Enabled)
        {
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

    public async Task EnqueueVerificationEmail(List<ShamiraPlayer> players, RecoveryEmailType emailType)
    {
        AssertEmailEnabled();

        logger.LogDebug("Enqueueing verification email");
        var job = jobManager.NewJob<SendRecoveryModeVerificationEmailJob>();
        job.Data = new SendRecoveryModeVerificationEmailJobData()
        {
            Domain = tenantContext.HostOdinId,
            Email = tenantContext.Email,
            Players = players,
            NonceId = await MakeNonce(),
            EmailType = emailType,
            Path = emailType == RecoveryEmailType.EnterRecoveryMode ? "verify-enter" : "verify-exit"
        };

#if DEBUG
        var link = job.CreateLink();
        logger.LogInformation("\n\n\n{link}\n\n\n", link);
#endif

        if (configuration.Mailgun.Enabled)
        {
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

    public async Task EnqueueFinalizeRecoveryEmail(FinalRecoveryInfo finalInfo, Guid finalRecoveryKey)
    {
        var nonceId = await MakeNonce(OdinSystemSerializer.Serialize(finalInfo));
        var tenant = tenantContext.HostOdinId;
        var link = BuildResetUrl($"https://{tenant}/owner/shamir-account-recovery", nonceId,
            finalRecoveryKey.ToString());

#if DEBUG
        logger.LogInformation("\n\n\n{link}\n\n\n", link);
#endif

        AssertEmailEnabled();

        if (configuration.Mailgun.Enabled) //for #debug state
        {
            var job = jobManager.NewJob<SendEmailJob>();
            job.Data = new SendEmailJobData()
            {
                Envelope = new Envelope
                {
                    To = [new NameAndEmailAddress { Email = tenantContext.Email }],
                    Subject = "We have assembled your recovery key!",
                    TextMessage = RecoveryEmails.FinalizeRecoveryUsingRecoveryKeyText(tenant, link),
                    HtmlMessage = RecoveryEmails.FinalizeRecoveryUsingRecoveryKeyHtml(tenant, link)
                },
            };

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

    private async Task<Guid> MakeNonce(string data = "")
    {
        var nonceId = Guid.NewGuid();
        var r = new NonceRecord()
        {
            id = nonceId,
            expiration = UnixTimeUtc.Now().AddHours(1), // 1 hour expiration
            data = data
        };

        await nonceTable.InsertAsync(r);
        return nonceId;
    }

    private void AssertEmailEnabled()
    {
        if (!configuration.Mailgun.Enabled)
        {
#if !DEBUG
            throw new OdinSystemException("Cannot enter into recovery mode when email is disabled");
#endif
        }
    }

    private static string BuildResetUrl(string baseUrl, Guid id, string token)
    {
        // Ensure URL-safe encoding if needed, but since both are hex ("N" format), they're safe
        return $"{baseUrl}?id={Uri.EscapeDataString(id.ToString("N"))}&token={Uri.EscapeDataString(token)}";
    }
}