using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core.Cryptography.Crypto;
using Odin.Core.Exceptions;
using Odin.Core.Serialization;
using Odin.Services.Email;
using Odin.Services.JobManagement;
using Odin.Services.JobManagement.Jobs;

namespace Odin.Services.ShamiraPasswordRecovery;

#nullable enable

public class SendRecoveryModeVerificationEmailJobData
{
    public string Domain { get; init; } = "";
    public string Path { get; init; } = "";
    public string Email { get; init; } = "";
    public Guid NonceId { get; init; }
}

//

public class SendRecoveryModeVerificationEmailJob(
    ILogger<SendRecoveryModeVerificationEmailJob> logger,
    IEmailSender emailSender) : AbstractJob
{
    
    public static readonly Guid JobTypeId = Guid.Parse("b864bf5b-1f4c-4b23-afc1-98c623e23017");
    public override string JobType => JobTypeId.ToString();

    public SendRecoveryModeVerificationEmailJobData Data { get; set; } = new ();

    public string CreateLink()
    {
        return EmailLinkHelper.BuildResetUrl($"https://{Data.Domain}/api/owner/v1/security/recovery/{Data.Path}", Data.NonceId, "");
    }

    public override async Task<JobExecutionResult> Run(CancellationToken cancellationToken)
    {
        ValidateJobData();

        logger.LogInformation("Send password recovery verification email: identity '{domain}' completed to {email}", Data.Domain, Data.Email);

        const string subject = "Homebase Password Recovery - Verify your email";

        var link = CreateLink();

        var envelope = new Envelope
        {
            To = [new NameAndEmailAddress { Email = Data.Email }],
            Subject = subject,
            TextMessage = RecoveryEmails.VerifyEmailText(Data.Email, Data.Domain, link),
            HtmlMessage = RecoveryEmails.VerifyEmailHtml(Data.Domain, link)
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
        Data = OdinSystemSerializer.DeserializeOrThrow<SendRecoveryModeVerificationEmailJobData>(json);
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
    }
}