using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core.Serialization;

namespace Odin.Services.JobManagement.Jobs;

#nullable enable

public class DummyJobData
{
    public string Echo { get; set; } = "";
}

public class DummyJob(ILogger<DummyJob> logger) : AbstractJob
{
    public static readonly Guid JobTypeId = Guid.Parse("783c4685-111f-4732-aebe-ab40f034da74");
    public override string JobType => JobTypeId.ToString();

    public DummyJobData Data { get; set; } = new();

    public override Task<JobExecutionResult> Run(CancellationToken cancellationToken)
    {
        logger.LogInformation("DummyJob says: {echo}", Data.Echo);
        return Task.FromResult(JobExecutionResult.Success());
    }

    public override string? SerializeJobData()
    {
        return OdinSystemSerializer.Serialize(Data);
    }

    public override void DeserializeJobData(string json)
    {
        Data = OdinSystemSerializer.DeserializeOrThrow<DummyJobData>(json);
    }
}

