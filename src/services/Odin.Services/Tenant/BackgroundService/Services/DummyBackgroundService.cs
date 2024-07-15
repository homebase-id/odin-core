using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Odin.Services.Tenant.BackgroundService.Services;

public sealed class DummyBackgroundService(ILogger<DummyBackgroundService> logger, Tenant tenant)
    : AbstractTenantBackgroundService
{
    private readonly Tenant _tenant = tenant;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            logger.LogDebug("DummyBackgroundService: Tenant '{tenant}' is running", _tenant.Name);
            await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
        }
    }
}

