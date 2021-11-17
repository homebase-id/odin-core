using System;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Quartz;
using Refit;
using Youverse.Core.Services.Transit;

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

        public Task Execute(IJobExecutionContext context)
        {
            InitializeHttpClient();

            _logger.LogInformation("Send Payload Job running now");
            var senders = _pendingTransfers.GetSenders();
            foreach (var sender in senders)
            {
                var b = new UriBuilder()
                {
                    Scheme = "https",
                    Host = sender,
                };

                //TODO: do this in parallel
                StokeOutbox(b.Uri);
            }

            return Task.CompletedTask;
        }

        private void InitializeHttpClient()
        {
            //TODO: add a certificate for the stoker
            // var handler = new HttpClientHandler();
            // handler.ClientCertificates.Add(cert);
            // handler.AllowAutoRedirect = false;
            //handler.ServerCertificateCustomValidationCallback
            //handler.SslProtocols = SslProtocols.None;// | SslProtocols.Tls13;

            //_client = new System.Net.Http.HttpClient(handler);
            this._client = new HttpClient();
        }

        private async Task StokeOutbox(object? uri)
        {
            var withPath = new Uri((Uri) uri, "/api/transit/background/stoke");
            _logger.LogInformation($"Stoke running for {withPath.ToString()}");
            Console.WriteLine($"Stoke running for {withPath.ToString()}");
            // var request = new HttpRequestMessage(HttpMethod.Post, withPath);
            // var response = _client.Send(request);

            var svc = RestService.For<ITransitClientToHostHttpClient>(_client);
            var response = await svc.ProcessOutbox();
            //TODO: needs information to determine if it should stoke again; and when

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning($"Background stoking for [{uri}] failed.");
            }
        }
    }
}