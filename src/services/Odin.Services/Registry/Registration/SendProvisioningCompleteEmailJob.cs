using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core.Exceptions;
using Odin.Core.Logging.CorrelationId;
using Odin.Services.Email;
using Odin.Services.Quartz;
using Quartz;

namespace Odin.Services.Registry.Registration;

public class SendProvisioningCompleteEmailScheduler(
    string domain,
    string email,
    string firstRunToken,
    TimeSpan fromNow) : AbstractJobScheduler
{
    public sealed override string SchedulingKey { get; } = Helpers.UniqueId();

    public override Task<(JobBuilder, List<TriggerBuilder>)> Schedule<TJob>(JobBuilder jobBuilder)
    {
        jobBuilder
            .WithRetry(20, TimeSpan.FromMinutes(1))
            .WithRetention(TimeSpan.FromMinutes(1))
            .UsingJobData("email", email)
            .UsingJobData("domain", domain)
            .UsingJobData("firstRunToken", firstRunToken);

        var triggerBuilders = new List<TriggerBuilder>
        {
            TriggerBuilder.Create()
                .StartAt(DateTimeOffset.Now + fromNow)
        };

        return Task.FromResult((jobBuilder, triggerBuilders));
    }
}

public class SendProvisioningCompleteEmailJob(
    ICorrelationContext correlationContext,
    ILogger<SendProvisioningCompleteEmailJob> logger,
    IEmailSender emailSender,
    IIdentityRegistrationService identityRegistrationService
    ) : AbstractJob(correlationContext)
{
    protected sealed override async Task Run(IJobExecutionContext context)
    {
        var jobData = context.JobDetail.JobDataMap;
        jobData.TryGetString("email", out var email);
        jobData.TryGetString("domain", out var domain);
        jobData.TryGetString("firstRunToken", out var firstRunToken);
        if (email == null || domain == null || firstRunToken == null)
        {
            // Sanity
            return;
        }

        // Certificate is not ready yet?
        if (!await identityRegistrationService.HasValidCertificate(domain))
        {
            // Throw an error so job manager retries the operation
            logger.LogInformation("Provisioning email: certificate not ready yet, scheduling a later check");
            throw new OdinSystemException("Provisioning email: certificate not ready yet");
        }

        const string subject = "Your new identity is ready";
        var firstRunlink = $"https://{domain}/owner/firstrun?frt={firstRunToken}";

        var envelope = new Envelope
        {
            To = [new NameAndEmailAddress { Email = email }],
            Subject = subject,
            TextMessage = RegistrationEmails.ProvisioningCompletedText(email, domain, firstRunlink),
            HtmlMessage = RegistrationEmails.ProvisioningCompletedHtml(email, domain, firstRunlink),
        };

        await emailSender.SendAsync(envelope);
    }
}
