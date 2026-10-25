using System;
using System.Collections.Generic;
using System.Net.Mail;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core.Exceptions;
using Odin.Core.Identity;
using Odin.Core.Storage.Database.Identity.Table;
using Odin.Core.Time;
using Odin.Services.AppNotifications.Push;
using Odin.Services.Apps;
using Odin.Services.Authentication.Owner;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Base;
using Odin.Services.Configuration;
using Odin.Services.Dns.Health;
using Odin.Services.Email;
using Odin.Services.JobManagement;
using Odin.Services.Peer.Outgoing.Drive;
using Odin.Services.Security.PasswordRecovery.Shamir;

namespace Odin.Services.Security.Email;

public class RecoveryNotifier(
    OdinConfiguration configuration,
    ILogger<RecoveryNotifier> logger,
    TenantContext tenantContext,
    TableNonce nonceTable,
    IJobManager jobManager,
    PushNotificationService pushNotificationService,
    IRecoveryNotificationSink notificationSink)
{
    /// <summary>A link's nonce data, without using it up: to check a request before it is used.</summary>
    public async Task<string> PeekNonceDataOrFail(Guid nonceId)
    {
        var record = await nonceTable.PeekAsync(nonceId) ?? throw new OdinClientException("Invalid Nonce");
        return record.data;
    }

    public async Task<string> GetNonceDataOrFail(Guid nonceId)
    {
        var record = await nonceTable.PopAsync(nonceId);
        if (record == null)
        {
            throw new OdinClientException("Invalid Nonce");
        }

        return record.data;
    }

    public async Task EnqueueVerifyRecoveryEmailAddress(MailAddress email)
    {
        logger.LogDebug("Enqueueing verify new recovery email address");

        var tenant = tenantContext.HostOdinId;
        var nonceId = await MakeNonce(email.Address);

        var link = BuildResetUrl($"https://{tenant}{OwnerApiPathConstants.SecurityRecoveryV1}/verify-email-fwd", nonceId, "");

        notificationSink.Notify(new RecoveryNotification(tenant, RecoveryNotificationKind.VerifyRecoveryEmail, link, nonceId));
#if DEBUG
        logger.LogInformation("\n\n\n{link}\n\n\n{nonceId}", link, nonceId);
#endif

        AssertEmailEnabled();

        if (configuration.Mailgun.Enabled)
        {
            await EnqueueEmailAsync(new Envelope
            {
                To = [new NameAndEmailAddress { Name = email.DisplayName, Email = email.Address }],
                Subject = "Please verify your new recovery email address!",
                TextMessage = RecoveryEmails.VerifyNewRecoveryEmailText(tenant, link),
                HtmlMessage = RecoveryEmails.VerifyNewRecoveryEmailHtml(tenant, link)
            });
        }
    }

    /// <param name="nonceData">Kept with the link's nonce: the recovering browser's public key, for a friend deal.</param>
    public async Task EnqueueVerificationEmail(List<ShamiraPlayer> players, RecoveryEmailType emailType, string nonceData = "")
    {
        AssertEmailEnabled();

        logger.LogDebug("Enqueueing verification email");
        var job = jobManager.NewJob<SendRecoveryModeVerificationEmailJob>();
        job.Data = new SendRecoveryModeVerificationEmailJobData()
        {
            Domain = tenantContext.HostOdinId,
            Email = tenantContext.Email,
            Players = players,
            NonceId = await MakeNonce(nonceData),
            EmailType = emailType,
            Path = emailType == RecoveryEmailType.EnterRecoveryMode ? "verify-enter" : "verify-exit"
        };

        var link = job.CreateLink();
        notificationSink.Notify(new RecoveryNotification(tenantContext.HostOdinId,
            emailType == RecoveryEmailType.EnterRecoveryMode
                ? RecoveryNotificationKind.EnterRecoveryMode
                : RecoveryNotificationKind.ExitRecoveryMode,
            link, job.Data.NonceId));
#if DEBUG
        if (emailType == RecoveryEmailType.EnterRecoveryMode)
        {
            logger.LogInformation("\n\n\n{link}\n\n\n{enterNonceId}", link, job.Data.NonceId);
        }
        else
        {
            logger.LogInformation("\n\n\n{link}\n\n\n{exitNonceId}", link, job.Data.NonceId);
        }
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

    /// <summary>
    /// Enough shards are in: the owner goes back to the browser where recovery started, which rebuilds
    /// the secret.  The email carries no link and no key (docs/zero-knowledge-recovery-plan.md,
    /// decision 11).
    /// </summary>
    public async Task EnqueueShardsCollectedEmail()
    {
        var tenant = tenantContext.HostOdinId;
        notificationSink.Notify(new RecoveryNotification(tenant, RecoveryNotificationKind.ShardsCollected, null, Guid.Empty));

        AssertEmailEnabled();

        if (configuration.Mailgun.Enabled)
        {
            await EnqueueEmailAsync(new Envelope
            {
                To = [new NameAndEmailAddress { Email = tenantContext.Email }],
                Subject = "Your recovery is ready",
                TextMessage = RecoveryEmails.ShardsCollectedText(tenant),
                HtmlMessage = RecoveryEmails.ShardsCollectedHtml(tenant)
            });
        }
    }

    /// <summary>
    /// As an automatic holder: emails <paramref name="dealer"/>'s recovery address, which the dealer
    /// sent and this holder checked against the hash it stored at the deal, a link that releases the
    /// shard (docs/zero-knowledge-recovery-plan.md, "Building the automated deal").  The link opens the
    /// dealer's recovery page, on any device.
    /// </summary>
    public async Task EnqueueReleaseShardEmail(MailAddress email, OdinId dealer, Guid shardId, string code)
    {
        AssertEmailEnabled();

        var holder = tenantContext.HostOdinId;
        var link = $"https://{dealer}/owner/shamir-account-recovery?fill={shardId:N}&code={Uri.EscapeDataString(code)}";
        notificationSink.Notify(new RecoveryNotification(dealer, RecoveryNotificationKind.ReleaseShard, link, shardId, holder));

        if (configuration.Mailgun.Enabled)
        {
            await EnqueueEmailAsync(new Envelope
            {
                To = [new NameAndEmailAddress { Email = email.Address }],
                Subject = $"Your recovery: a piece from {holder}",
                TextMessage = RecoveryEmails.ReleaseShardText(dealer, holder, link),
                HtmlMessage = RecoveryEmails.ReleaseShardHtml(dealer, holder, link)
            });
        }
    }

    public async Task NotifyUser(OdinId odinId, RecoveryInfo recoveryInfo, IOdinContext odinContext,
        DnssecHealthResult dnssecAttention = null,
        IReadOnlyCollection<string> mailAttention = null)
    {
        if (configuration.Mailgun.Enabled) //for #debug state
        {
            var email = recoveryInfo.Email;
            await EnqueueEmailAsync(new Envelope
            {
                To = [new NameAndEmailAddress { Email = email }],
                Subject = "Your Homebase Account Recovery Risk Report",
                TextMessage = RecoveryEmails.FormatRecoveryRiskStatusText(odinId, recoveryInfo, dnssecAttention, mailAttention),
                HtmlMessage = RecoveryEmails.FormatRecoveryRiskStatusHtml(odinId, recoveryInfo, dnssecAttention, mailAttention)
            });
        }

        await pushNotificationService.EnqueueNotification(odinId, new AppNotificationOptions()
            {
                AppId = SystemAppConstants.SystemAppId,
                TypeId = ShamirConfigurationService.SecurityRiskReportNotificationTypeId,
                TagId = odinId,
                Silent = false,
                PeerSubscriptionId = default,
                Recipients = [odinId],
                UnEncryptedMessage = "Your security risk report has been generated.  Tap to review..."
                // UnEncryptedJson = OdinSystemSerializer.Serialize(new
                // {
                //     IntroducerOdinId = introducer,
                //     Introduction = introduction,
                // })
            },
            OdinContextUpgrades.UsePermissions(odinContext, PermissionKeys.SendPushNotifications));
    }


    private async Task EnqueueEmailAsync(Envelope envelope)
    {
        var job = jobManager.NewJob<SendEmailJob>();
        job.Data = new SendEmailJobData { Envelope = envelope };

        await jobManager.ScheduleJobAsync(job, new JobSchedule
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

        await nonceTable.InsertAsync(r);
        return nonceId;
    }

    /// <summary>
    /// Recovery links only reach the owner by email, so without Mailgun recovery is refused --
    /// except on a development host, where the links are read from the log or the
    /// <see cref="IRecoveryNotificationSink"/> instead.
    /// </summary>
    private void AssertEmailEnabled()
    {
        if (!configuration.Mailgun.Enabled && !configuration.Development.Enabled)
        {
            throw new OdinSystemException("Cannot enter into recovery mode when email is disabled");
        }
    }

    private static string BuildResetUrl(string baseUrl, Guid id, string token)
    {
        // Ensure URL-safe encoding if needed, but since both are hex ("N" format), they're safe
        return $"{baseUrl}?id={Uri.EscapeDataString(id.ToString("N"))}&token={Uri.EscapeDataString(token)}";
    }
}