using System;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Quartz;
using Refit;
using Youverse.Core.Identity;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Transit.Outbox;
using Youverse.Core.Services.Workers.Cron;

namespace Youverse.Core.Services.Workers.Transit
{
    /// <summary>
    /// Sends a payload to a set of recipients
    /// </summary>
//    [DisallowConcurrentExecution]
    public class StokeOutboxJob : IJob
    {
        private HttpClient _client;
        private readonly ILogger<StokeOutboxJob> _logger;
        private readonly IPendingTransfersService _pendingTransfers;

        public StokeOutboxJob(ILogger<StokeOutboxJob> logger, IPendingTransfersService pendingTransfers)
        {
            _logger = logger;
            _pendingTransfers = pendingTransfers;
        }

        public async Task Execute(IJobExecutionContext context)
        {
            _logger.LogInformation("Send Payload Job running now");
            var (senders, marker) = await _pendingTransfers.GetIdentities();
            foreach (var sender in senders)
            {
                try
                {
                    //TODO: do this in parallel
                    StokeOutbox(sender);
                }
                catch (Exception e)
                {
                    Console.WriteLine(e);
                    _pendingTransfers.MarkFailure(marker);
                }
            }

            _pendingTransfers.MarkComplete(marker);
        }

        private async Task StokeOutbox(OdinId identity)
        {
            _logger.LogInformation($"Stoke running for {identity}");

            var svc = SystemHttpClient.CreateHttps<IOutboxHttpClient>(identity);
            var response = await svc.ProcessOutbox(batchSize: 1);

            //TODO: needs information to determine if it should stoke again; and when

            if (!response.IsSuccessStatusCode)
            {
                //TODO: need to log an error here and notify sys admins?
                _logger.LogWarning($"Background stoking for [{identity}] failed.");
            }
        }
    }
}