using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Quartz;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Certificate;
using Youverse.Core.Services.Configuration;
using Youverse.Core.Services.Registry;

namespace Youverse.Core.Services.Workers.Certificate
{
    /// <summary>
    /// Looks for certificates that require renewal and queues their renewal
    /// </summary>
    [DisallowConcurrentExecution]
    // ReSharper disable once ClassNeverInstantiated.Global
    public class EnsureIdentityHasValidCertificateJob : IJob
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<EnsureIdentityHasValidCertificateJob> _logger;
        private readonly IIdentityRegistry _registry;
        private readonly YouverseConfiguration _config;

        public EnsureIdentityHasValidCertificateJob(
            IServiceProvider serviceProvider,
            ILogger<EnsureIdentityHasValidCertificateJob> logger,
            IIdentityRegistry registry,
            YouverseConfiguration config)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
            _registry = registry;
            _config = config;
        }

        public async Task Execute(IJobExecutionContext context)
        {
            _logger.LogDebug("Executing job {job} on thread {managedThreadId}", GetType().Name, Environment.CurrentManagedThreadId);

            var certificateServiceFactory = _serviceProvider.GetRequiredService<ICertificateServiceFactory>();
            
            var tasks = new List<Task>();
            var identities = await _registry.GetList();
            foreach (var identity in identities.Results)
            {
                var tenantContext =
                    TenantContext.Create(identity.Id, identity.PrimaryDomainName, _config.Host.TenantDataRootPath, null,_config.Host.TenantPayloadRootPath);
                var tc = certificateServiceFactory.Create(tenantContext.SslRoot);
                var task = tc.RenewIfAboutToExpire(identity);
                tasks.Add(task);
            }

            await Task.WhenAll(tasks);

            _logger.LogDebug("Completed job {job} on thread {managedThreadId}", GetType().Name, Environment.CurrentManagedThreadId);
        }
    }
}