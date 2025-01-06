using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core.Exceptions;
using Odin.Core.Serialization;
using Odin.Services.Email;
using Odin.Services.JobManagement;
using Odin.Services.JobManagement.Jobs;

namespace Odin.Services.Registry.Registration;

#nullable enable

public class SendProvisioningCompleteEmailJobData
{
    public string Domain { get; set; } = "";
    public string Email { get; set; } = "";
    public string FirstRunToken { get; set; } = "";
}

//

public class SendProvisioningCompleteEmailJob(
    ILogger<SendProvisioningCompleteEmailJob> logger,
    IEmailSender emailSender,
    IIdentityRegistrationService identityRegistrationService) : AbstractJob
{
    public static readonly Guid JobTypeId = Guid.Parse("37b89769-b428-428d-9d7d-697058184370");
    public override string JobType => JobTypeId.ToString();

    public SendProvisioningCompleteEmailJobData Data { get; set; } = new ();

    public override async Task<JobExecutionResult> Run(CancellationToken cancellationToken)
    {
        ValidateJobData();

        // Certificate is not ready yet?
        if (!await identityRegistrationService.HasValidCertificate(Data.Domain))
        {
            // Throw an error so job manager retries the operation later
            logger.LogInformation("Provisioning email: certificate not ready yet, scheduling a later check");
            throw new OdinSystemException("Provisioning email: certificate not ready yet");
        }

        logger.LogInformation("Send email: provisioning of domain '{domain}' completed to {email}", Data.Domain, Data.Email);

        const string subject = "Your new identity is ready";
        var firstRunlink = $"https://{Data.Domain}/owner/firstrun?frt={Data.FirstRunToken}";

        var envelope = new Envelope
        {
            To = [new NameAndEmailAddress { Email = Data.Email }],
            Subject = subject,
            TextMessage = RegistrationEmails.ProvisioningCompletedText(Data.Email, Data.Domain, firstRunlink),
            HtmlMessage = RegistrationEmails.ProvisioningCompletedHtml(Data.Domain, firstRunlink)
        };

        await emailSender.SendAsync(envelope);

        return JobExecutionResult.Success();
    }

    //

    public override string SerializeJobData()
    {
        return OdinSystemSerializer.Serialize(Data);
    }

    //

    public override void DeserializeJobData(string json)
    {
        Data = OdinSystemSerializer.DeserializeOrThrow<SendProvisioningCompleteEmailJobData>(json);
    }

    //

    private void ValidateJobData()
    {
        if (string.IsNullOrEmpty(Data.Domain))
        {
            throw new OdinSystemException($"{nameof(Data.Domain)} is missing");
        }
        if (string.IsNullOrEmpty(Data.Email))
        {
            throw new OdinSystemException($"{nameof(Data.Email)} is missing");
        }
        if (string.IsNullOrEmpty(Data.FirstRunToken))
        {
            throw new OdinSystemException($"{nameof(Data.FirstRunToken)} is missing");
        }
    }
}

