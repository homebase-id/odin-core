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
        public DotYouHttpClientProxy(X509Certificate2 cert)
        {
            _clientCertificate = cert;
        }

        public async Task<bool> Post<T>(DotYouIdentity dotYouId, string path, T payload)
        {
            HttpClientHandler handler = new();
            handler.ClientCertificates.Add(_clientCertificate);
            handler.ClientCertificateOptions = ClientCertificateOption.Manual;

            using (HttpClient client = new HttpClient(handler))
            {
                UriBuilder b = new UriBuilder();
                b.Scheme = "https";
                b.Host = dotYouId;
                b.Path = path;

                var response = await client.PostAsJsonAsync(b.Uri, payload);

                return response.IsSuccessStatusCode;
            }
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
