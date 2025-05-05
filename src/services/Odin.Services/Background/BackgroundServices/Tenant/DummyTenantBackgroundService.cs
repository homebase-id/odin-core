using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Services.Base;

namespace Odin.Services.Background.BackgroundServices.Tenant;

public sealed class DummyTenantBackgroundService(
    ILogger<DummyTenantBackgroundService> logger,
    TenantContext tenantContext)
    : AbstractBackgroundService(logger)
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            logger.LogDebug("DummyTenantBackgroundService: Tenant '{tenant}' is running", tenantContext.HostOdinId);
            await SleepAsync(TimeSpan.FromSeconds(1), stoppingToken);
        }
    }
}

