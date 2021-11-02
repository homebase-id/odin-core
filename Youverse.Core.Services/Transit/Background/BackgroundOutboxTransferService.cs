using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Youverse.Core.Identity;
using Youverse.Core.Services.Registry;

namespace Youverse.Core.Services.Transit.Background
{
    public class BackgroundOutboxTransferService : IHostedService, IDisposable
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<BackgroundOutboxTransferService> _logger;
        private readonly Dictionary<DotYouIdentity, Timer> _timers = new();
        private HttpClient _client;

        public BackgroundOutboxTransferService(ILogger<BackgroundOutboxTransferService> logger, IServiceProvider serviceProvider)
        {
            _logger = logger;
            _serviceProvider = serviceProvider;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            //load up timers with all digital identities managed on this host
            var reg = _serviceProvider.GetRequiredService<IIdentityContextRegistry>();

            InitializeHttpClient();

            foreach (var domain in reg.GetDomains())
            {
                var b = new UriBuilder()
                {
                    Scheme = "https",
                    Host = domain,
                };
                var t = new Timer(StokeOutbox, b.Uri, TimeSpan.Zero, TimeSpan.FromSeconds(12));
                _timers.Add((DotYouIdentity)domain, t);
                Console.WriteLine($"Added timer for domain {domain}");
            }

            return Task.CompletedTask;
            //return base.StartAsync(cancellationToken);
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            Console.WriteLine("Outbox stoker is shutting down.");

            foreach (var timer in _timers)
            {
                try
                {
                    timer.Value.Change(Timeout.Infinite, 0);
                }
                catch (ObjectDisposedException)
                {
                    //gulp.  yuck!
                }
            }

            return Task.CompletedTask;
        }

        public void Dispose()
        {
            foreach (var timer in _timers)
            {
                timer.Value?.Dispose();
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

        private void StokeOutbox(object? uri)
        {
            var withPath = new Uri((Uri)uri, "/api/transit/background/stoke");
            Console.WriteLine($"Stoke running for {withPath.ToString()}");
            var request = new HttpRequestMessage(HttpMethod.Post, withPath);
            var response = _client.Send(request);

            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine($"Background stoking for [{uri}] failed.");
                _logger.LogWarning($"Background stoking for [{uri}] failed.");
            }
        }
    }
}