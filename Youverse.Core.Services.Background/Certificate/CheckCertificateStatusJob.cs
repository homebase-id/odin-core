using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Quartz;
using Youverse.Core.Services.Registry;

namespace Youverse.Core.Services.Workers.Certificate
{
    /// <summary>
    /// Looks for certificates that require renewal and queues their renewal
    /// </summary>
    [DisallowConcurrentExecution]
    public class CheckCertificateStatusJob : IJob
    {
        private HttpClient _client;
        private readonly ILogger<CheckCertificateStatusJob> _logger;
        private readonly IIdentityRegistry _registry;

        public CheckCertificateStatusJob(ILogger<CheckCertificateStatusJob> logger, IIdentityRegistry registry)
        {
            _logger = logger;
            _registry = registry;
        }

        public async Task Execute(IJobExecutionContext context)
        {
            //scan the list of registered identities

            //load their certificates 

            //if needs renewal, 

            var identities = await _registry.GetList(PageOptions.All);

            foreach (var ident in identities.Results)
            {
                
            }
        }
    }
}