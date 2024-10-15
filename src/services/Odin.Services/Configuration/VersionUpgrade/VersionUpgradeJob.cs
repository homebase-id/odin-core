using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core.Exceptions;
using Odin.Core.Serialization;
using Odin.Services.JobManagement;

namespace Odin.Services.Configuration.VersionUpgrade;

#nullable enable

//

public class VersionUpgradeJob(
    ILogger<VersionUpgradeJob> logger) : AbstractJob
{
    public VersionUpgradeJobData Data { get; set; } = new();

    public override async Task<JobExecutionResult> Run(CancellationToken cancellationToken)
    {
        ValidateJobData();

        // do the thing
        await Task.CompletedTask;
        return JobExecutionResult.Success();
    }

    public override string? CreateJobHash()
    {
        return base.CreateJobHash();
    }

    //

    public override string SerializeJobData()
    {
        return OdinSystemSerializer.Serialize(Data);
    }

    //

    public override void DeserializeJobData(string json)
    {
        Data = OdinSystemSerializer.DeserializeOrThrow<VersionUpgradeJobData>(json);
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