using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Odin.Services.Tenant.BackgroundService;

public sealed class DummyBackgroundService(ILogger<DummyBackgroundService> logger, Tenant tenant)
    : AbstractTenantBackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            logger.LogDebug("XXXXXXXXXXXXXXXXXXXXXXX Tenant '{tenant}' is running", tenant.Name);
            await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
        }
    }
}

