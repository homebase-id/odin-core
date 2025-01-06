using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core.Serialization;
using Odin.Services.Configuration;
using Odin.Services.JobManagement;
using Odin.Services.JobManagement.Jobs;
using Odin.Services.Registry;

namespace Odin.Services.Admin.Tenants.Jobs;
#nullable enable

//

public class ExportTenantJobData
{
    public string Domain { get; set; } = string.Empty;
    public string TargetPath { get; set; } = string.Empty;
}

//

public class ExportTenantJob(
    ILogger<ExportTenantJob> logger,
    OdinConfiguration config,
    IIdentityRegistry identityRegistry) : AbstractJob
{
    public static readonly Guid JobTypeId = Guid.Parse("d58812e0-b087-48e3-b115-2a9f92dd671a");
    public override string JobType => JobTypeId.ToString();

    public ExportTenantJobData Data { get; set; } = new ();

    //

    public override async Task<JobExecutionResult> Run(CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(Data.Domain))
        {
            throw new InvalidOperationException("Domain is required");
        }

        logger.LogDebug("Starting export tenant {domain}", Data.Domain);

        var sw = Stopwatch.StartNew();
        Data.TargetPath = await identityRegistry.CopyRegistration(Data.Domain, config.Admin.ExportTargetPath);

        logger.LogDebug("Finished export tenant {domain} in {elapsed}s", Data.Domain, sw.ElapsedMilliseconds / 1000.0);

        return JobExecutionResult.Success();
    }

    //

    public override string? SerializeJobData()
    {
        return OdinSystemSerializer.Serialize(Data);
    }

    //

    public override void DeserializeJobData(string json)
    {
        Data = OdinSystemSerializer.DeserializeOrThrow<ExportTenantJobData>(json);
    }
}

//




