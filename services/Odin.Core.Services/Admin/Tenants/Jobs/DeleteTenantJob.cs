using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core.Services.Registry;
using Quartz;

namespace Odin.Core.Services.Admin.Tenants.Jobs;

[DisallowConcurrentExecution]
public class DeleteTenantJob : IJob
{
    public const string JobGroup = "delete-tenant";

    private readonly ILogger<DeleteTenantJob> _logger;
    private readonly IIdentityRegistry _identityRegistry;

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
            await _identityRegistry.DeleteRegistration(domain);

            _logger.LogDebug("Finished delete tenant {domain} in {elapsed}s", domain, sw.ElapsedMilliseconds / 1000.0);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Error deleting tenant: {error}", e.Message);
        }
    }
}

