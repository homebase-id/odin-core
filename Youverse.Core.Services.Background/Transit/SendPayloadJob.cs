using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Quartz;

namespace Youverse.Core.Services.Workers.Transit
{
    /// <summary>
    /// Sends a payload to a set of recipients
    /// </summary>
//    [DisallowConcurrentExecution]
    public class SendPayloadJob : IJob
    {
        private readonly ILogger<SendPayloadJob> _logger;

        public SendPayloadJob(ILogger<SendPayloadJob> logger)
        {
            _logger = logger;
        }

        public Task Execute(IJobExecutionContext context)
        {
            //for a given tenant, use the transit engine to send a transfer
            _logger.LogInformation("Send Payload Job running now");
            return Task.CompletedTask;
        }
    }
}