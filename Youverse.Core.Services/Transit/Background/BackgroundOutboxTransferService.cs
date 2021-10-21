using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Youverse.Core.Services.Transit.Background
{
    public class BackgroundOutboxTransferService : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<BackgroundOutboxTransferService> _logger;

        public BackgroundOutboxTransferService(ILogger<BackgroundOutboxTransferService> logger, IServiceProvider serviceProvider)
        {
            _logger = logger;
            _serviceProvider = serviceProvider;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            //Note: this line is crucial to allow the rest of the startup
            //process to complete before we run this background service
            //https://stackoverflow.com/questions/61866319/start-ihostedservice-after-configure
            await Task.Yield();

            Console.WriteLine("BackgroundTransferService is starting");

            //TODO: test for multi-tenant
            // using (var scope = _serviceProvider.CreateScope())
            // {
            //     scope.ServiceProvider.GetService<>()
            // }

            var queue = _serviceProvider.GetRequiredService<OutboxQueueService>();
            var transferService = _serviceProvider.GetRequiredService<TransitService>();

            while (!stoppingToken.IsCancellationRequested)
            {
                //pick up the files from the outbox
                var batch = queue.GetNextBatch();
                await transferService.SendBatchNow(batch);
            }

            Console.WriteLine("BackgroundTransferService is shutting down.");
            _logger.LogDebug("BackgroundTransferService is shutting down.");
        }
    }
}