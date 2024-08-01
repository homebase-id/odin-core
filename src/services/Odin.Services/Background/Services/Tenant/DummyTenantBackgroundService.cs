using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Odin.Services.Background.Services.Tenant;

public sealed class DummyTenantBackgroundService(
    ILogger<DummyTenantBackgroundService> logger, 
    Odin.Services.Tenant.Tenant tenant)
    : AbstractBackgroundService(logger)
{
    private readonly Odin.Services.Tenant.Tenant _tenant = tenant;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            logger.LogDebug("DummyTenantBackgroundService: Tenant '{tenant}' is running", _tenant.Name);
            await SleepAsync(TimeSpan.FromSeconds(1), stoppingToken);
        }
    }
}

