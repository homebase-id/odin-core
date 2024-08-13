using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core.Serialization;
using Odin.Services.JobManagement;

namespace Odin.Hosting.Controllers.Job;

#nullable enable

public class DummyJobData
{
    public string Echo { get; set; } = "";
}

public class DummyJob(ILogger<DummyJob> logger) : AbstractJob
{
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

