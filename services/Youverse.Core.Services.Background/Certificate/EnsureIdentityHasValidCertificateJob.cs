using System;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Quartz;
using Refit;
using Youverse.Core.Identity;
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
        private readonly ILogger<EnsureIdentityHasValidCertificateJob> _logger;
        private readonly IIdentityRegistry _registry;

        public EnsureIdentityHasValidCertificateJob(ILogger<EnsureIdentityHasValidCertificateJob> logger, IIdentityRegistry registry)
        {
            _logger = logger;
            _registry = registry;
        }

        public async Task Execute(IJobExecutionContext context)
        {
            await _registry.EnsureCertificates();
        }
        
    }
}