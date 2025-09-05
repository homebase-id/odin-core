using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Odin.Core.Exceptions;
using Odin.Core.Serialization;
using Odin.Services.Configuration;
using Odin.Services.Email;
using Odin.Services.JobManagement;
using Odin.Services.JobManagement.Jobs;

namespace Odin.Services.ShamiraPasswordRecovery;

#nullable enable

public class SendEmailJobData
{
    public Envelope Envelope { get; init; } = new();
}

//

public class SendEmailJob(
    IEmailSender emailSender,
    OdinConfiguration configuration) : AbstractJob
{
    public static readonly Guid JobTypeId = Guid.Parse("20a6099e-974f-4266-9025-33311a421206");
    public override string JobType => JobTypeId.ToString();

    public SendEmailJobData Data { get; set; } = new();

    public override async Task<JobExecutionResult> Run(CancellationToken cancellationToken)
    {
        if (!configuration.Mailgun.Enabled)
        {
            return JobExecutionResult.Abort();
        }

        ValidateJobData();

        Envelope envelope = Data.Envelope;
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
        Data = OdinSystemSerializer.DeserializeOrThrow<SendEmailJobData>(json);
    }

    //

    private void ValidateJobData()
    {
        if (string.IsNullOrEmpty(Data.Envelope.Subject))
        {
            throw new OdinSystemException($"{nameof(Data.Envelope.Subject)} is missing");
        }

        if (Data.Envelope.To.Any(f => string.IsNullOrEmpty(f.Email)))
        {
            throw new OdinSystemException($"{nameof(Data.Envelope.To)} is missing");
        }
        
        if (string.IsNullOrEmpty(Data.Envelope.HtmlMessage))
        {
            throw new OdinSystemException($"{nameof(Data.Envelope.HtmlMessage)} is missing");
        }
        
        if (string.IsNullOrEmpty(Data.Envelope.TextMessage))
        {
            throw new OdinSystemException($"{nameof(Data.Envelope.TextMessage)} is missing");
        }
    }
}