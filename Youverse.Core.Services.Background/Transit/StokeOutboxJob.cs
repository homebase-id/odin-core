using System;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Quartz;
using Refit;
using Youverse.Core.Identity;
using Youverse.Core.Services.Transit;
using Youverse.Core.Services.Transit.Outbox;

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
            InitializeHttpClient();

            _logger.LogInformation("Send Payload Job running now");
            var senders = await _pendingTransfers.GetSenders();
            foreach (var sender in senders)
            {
                //TODO: do this in parallel
                StokeOutbox(sender);
            }
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

        private async Task StokeOutbox(DotYouIdentity sender)
        {
            var uri = new UriBuilder()
            {
                Scheme = "https",
                Host = sender
            }.Uri;
            
            
            _logger.LogInformation($"Stoke running for {sender}");
            
            _client.BaseAddress = uri;
            _client.DefaultRequestHeaders.Add("SY4829", Guid.Parse("a1224889-c0b1-4298-9415-76332a9af80e").ToString());
            var svc = RestService.For<IOutboxHttpClient>(_client);
            
            var response = await svc.ProcessOutbox();
            //TODO: needs information to determine if it should stoke again; and when

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning($"Background stoking for [{uri}] failed.");
            }
        }
    }
}