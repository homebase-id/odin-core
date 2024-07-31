using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Services.Certificate;
using Odin.Services.Configuration;
using Odin.Services.Registry;

namespace Odin.Services.Background.Services.System;

public class UpdateCertificatesBackgroundService(
    ILogger<UpdateCertificatesBackgroundService> logger,
    OdinConfiguration odinConfig,
    ICertificateServiceFactory certificateServiceFactory,
    IIdentityRegistry registry)
    : AbstractBackgroundService(logger)
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(odinConfig.Job.EnsureCertificateProcessorIntervalSeconds);
        
        while (!stoppingToken.IsCancellationRequested)
        {
            logger.LogDebug("Checking certificates");
            
            var tasks = new List<Task>();
            var identities = await registry.GetList();
            foreach (var identity in identities.Results)
            {
                var tenantContext = registry.CreateTenantContext(identity);
                var tc = certificateServiceFactory.Create(tenantContext.SslRoot);
                var task = tc.RenewIfAboutToExpire(identity);
                tasks.Add(task);
            }

            await Task.WhenAll(tasks);
            tasks.Clear();

            await SleepAsync(interval, stoppingToken);
        }
    }
}