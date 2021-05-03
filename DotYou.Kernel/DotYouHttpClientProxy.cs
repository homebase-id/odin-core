using DotYou.Types;
using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;


namespace DotYou.Kernel
{
    public class DotYouHttpClientProxy : IDotYouHttpClientProxy
    {
        X509Certificate2 _clientCertificate;
        HttpClient _client;
        public DotYouHttpClientProxy(DotYouContext context)
        {
            //TODO: Not sure if we need to keep an open instance of the certificate 
            _clientCertificate = context.TenantCertificate.LoadCertificate();

            HttpClientHandler handler = new();
            handler.ClientCertificates.Add(_clientCertificate);
            handler.ClientCertificateOptions = ClientCertificateOption.Manual;

            _client = new HttpClient(handler);
            UriBuilder b = new UriBuilder();
            b.Scheme = "https";
            b.Host = context.DotYouId;

            _client.BaseAddress = b.Uri;

            //TODO: add any headers
        }

        public void Dispose()
        {
            if(_client != null)
            {
                _client.Dispose();
            }

            if(_clientCertificate != null)
            {
                _clientCertificate.Dispose();
            }
        }

        public async Task<bool> Post<T>(string path, T payload)
        {
            HttpClientHandler handler = new();
            handler.ClientCertificates.Add(_clientCertificate);
            handler.ClientCertificateOptions = ClientCertificateOption.Manual;

            var response = await _client.PostAsJsonAsync(path, payload);
            return response.IsSuccessStatusCode;
        }

    }

    //
    internal class SuppressExecutionContextHandler : DelegatingHandler
    {
        public SuppressExecutionContextHandler(HttpMessageHandler handler) : base(handler)
        {
            InnerHandler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            // NOTE: We DO NOT want to 'await' the task inside this using. We're just suppressing execution context flow
            // while the task itself is created (which is what would capture the context). After that we just return the
            // (now detached task) to the caller.
            Task<HttpResponseMessage> t;

            using (ExecutionContext.SuppressFlow())
            {
                t = Task.Run(() => base.SendAsync(request, cancellationToken));
            }
            return t;
        }
    }
}
