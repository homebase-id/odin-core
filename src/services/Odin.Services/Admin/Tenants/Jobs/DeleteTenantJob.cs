using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core.Serialization;
using Odin.Services.JobManagement;
using Odin.Services.JobManagement.Jobs;
using Odin.Services.Registry;

namespace Odin.Services.Admin.Tenants.Jobs;
#nullable enable

public class DeleteTenantJobData
{
    public string Domain { get; set; } = string.Empty;
}

public class DeleteTenantJob(ILogger<DeleteTenantJob> logger, IIdentityRegistry identityRegistry) : AbstractJob
{
    public static readonly Guid JobTypeId = Guid.Parse("324fa88f-2ef6-404a-a511-9ef65ea841af");
    public override string JobType => JobTypeId.ToString();

    public DeleteTenantJobData Data { get; set; } = new ();

    public override async Task<JobExecutionResult> Run(CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(Data.Domain))
        {
            throw new InvalidOperationException("Domain is required");
        }

        logger.LogDebug("Starting delete tenant {domain}", Data.Domain);
        var sw = Stopwatch.StartNew();
        await identityRegistry.ToggleDisabled(Data.Domain, true);
        await identityRegistry.DeleteRegistration(Data.Domain);
        logger.LogDebug("Finished delete tenant {domain} in {elapsed}s", Data.Domain, sw.ElapsedMilliseconds / 1000.0);

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
        Data = OdinSystemSerializer.DeserializeOrThrow<DeleteTenantJobData>(json);
    }
}


