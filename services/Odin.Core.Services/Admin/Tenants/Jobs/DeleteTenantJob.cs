using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core.Exceptions;
using Odin.Core.Services.Quartz;
using Odin.Core.Services.Registry;
using Quartz;

namespace Odin.Core.Services.Admin.Tenants.Jobs;
#nullable enable

[DisallowConcurrentExecution]
public class DeleteTenantJob : IExclusiveJob
{
    public const string JobGroup = "delete-tenant";

    private readonly ILogger<DeleteTenantJob> _logger;
    private readonly IIdentityRegistry _identityRegistry;

    private readonly JobState _state = new ();
    private volatile bool _isDone;

    public DeleteTenantJob(ILogger<DeleteTenantJob> logger, IIdentityRegistry identityRegistry)
    {
        _logger = logger;
        _identityRegistry = identityRegistry;
    }

    //

    public async Task Execute(IJobExecutionContext context)
    {
        var domain = (string)context.JobDetail.JobDataMap["domain"];
        try
        {
            _logger.LogDebug("Starting delete tenant {domain}", domain);

            var sw = Stopwatch.StartNew();
            await _identityRegistry.ToggleDisabled(domain, true);
            await _identityRegistry.DeleteRegistration(domain);
            _state.Status = JobStatusEnum.Completed;

            _logger.LogDebug("Finished delete tenant {domain} in {elapsed}s", domain, sw.ElapsedMilliseconds / 1000.0);
        }
        catch (Exception e)
        {
            _state.Status = JobStatusEnum.Failed;
            _state.Error = e is OdinClientException ? e.Message : $"Internal error exporting tenant {domain}";
            _logger.LogError(e, "Error deleting tenant: {error}", e.Message);
        }
        _isDone = true;
    }

    //

    public IJobState State => _state;
    public bool IsDone => _isDone;
}

