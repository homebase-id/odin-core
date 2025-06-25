using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Services.Certificate;
using Odin.Services.Configuration;
using Odin.Services.Registry;

namespace Odin.Services.Background.BackgroundServices.System;

public class UpdateCertificatesBackgroundService(
    ILogger<UpdateCertificatesBackgroundService> logger,
    OdinConfiguration odinConfig,
    ICertificateService certificateService,
    IIdentityRegistry registry,
    ISystemDomains systemDomains)
    : AbstractBackgroundService(logger)
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(odinConfig.BackgroundServices.EnsureCertificateProcessorIntervalSeconds);
        
        while (!stoppingToken.IsCancellationRequested)
        {
            logger.LogDebug("{service} is running", GetType().Name);
            
            var tasks = new List<Task>();
            var identities = await registry.GetList();
            foreach (var identity in identities.Results)
            {
                var task = certificateService.RenewIfAboutToExpireAsync(
                    identity.PrimaryDomainName,
                    identity.GetSans(),
                    stoppingToken);
                tasks.Add(task);
            }

            foreach (var systemDomain in systemDomains.Get())
            {
                var task = certificateService.RenewIfAboutToExpireAsync(
                    systemDomain,
                    stoppingToken);
                tasks.Add(task);
            }

            await Task.WhenAll(tasks);
            tasks.Clear();

            logger.LogDebug("{service} is sleeping for {SleepDuration}", GetType().Name, interval);
            await SleepAsync(interval, stoppingToken);
        }
    }
}