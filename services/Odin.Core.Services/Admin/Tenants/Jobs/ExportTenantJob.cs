using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core.Exceptions;
using Odin.Core.Services.Configuration;
using Odin.Core.Services.Quartz;
using Odin.Core.Services.Registry;
using Quartz;

namespace Odin.Core.Services.Admin.Tenants.Jobs;
#nullable enable

[DisallowConcurrentExecution]
public class ExportTenantJob : IExclusiveJob
{
    public const string JobGroup = "export-tenant";
    public string JobId { get; } = Guid.NewGuid().ToString();

    private readonly ILogger<DeleteTenantJob> _logger;
    private readonly IIdentityRegistry _identityRegistry;
    private readonly OdinConfiguration _config;

    private readonly ExportTenantJobState _jobState = new ();
    private volatile bool _isDone;

    public ExportTenantJob(ILogger<DeleteTenantJob> logger, IIdentityRegistry identityRegistry, OdinConfiguration config)
    {
        _logger = logger;
        _identityRegistry = identityRegistry;
        _config = config;
    }

    //

    public async Task Execute(IJobExecutionContext context)
    {
        var domain = (string)context.JobDetail.JobDataMap["domain"];
        try
        {
            _logger.LogDebug("Starting export tenant {domain}", domain);

            var sw = Stopwatch.StartNew();
            _jobState.TargetPath = await _identityRegistry.CopyRegistration(domain, _config.Admin.ExportTargetPath);
            _jobState.Status = JobStatusEnum.Completed;

            _logger.LogDebug("Finished export tenant {domain} in {elapsed}s", domain, sw.ElapsedMilliseconds / 1000.0);
        }
        catch (Exception e)
        {
            _jobState.Status = JobStatusEnum.Failed;
            _jobState.Error = e is OdinClientException ? e.Message : $"Internal error exporting tenant {domain}";
            _logger.LogError(e, "Error exporting tenant: {error}", e.Message);
        }
        _isDone = true;
    }

    //

    public IJobState State => _jobState;
    public bool IsDone => _isDone;
}

public class ExportTenantJobState : IJobState
{
    public JobStatusEnum Status { get; set; }
    public string? Error { get; set; }
    public string TargetPath { get; set; } = "";
}
