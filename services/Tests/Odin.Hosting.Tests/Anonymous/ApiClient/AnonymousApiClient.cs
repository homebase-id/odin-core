using System;
using System.Net.Http;
using Odin.Core.Storage;

namespace Odin.Hosting.Tests.Anonymous.ApiClient
{
    public class AnonymousApiClient
    {
        private readonly TestIdentity _identity;

        public AnonymousApiClient(TestIdentity identity)
        {
            _identity = identity;
        }

        public TestIdentity Identity => _identity;

        /// <summary>
        /// Creates an http client that has a cookie jar but no authentication tokens.  This is useful for testing token exchanges.
        /// </summary>
        /// <returns></returns>
        public HttpClient CreateAnonymousApiHttpClient(FileSystemType fileSystemType = FileSystemType.Standard)
        {
            throw new NotImplementedException("This must be changed to use httpclient pooling");
            // var cookieJar = new CookieContainer();
            // HttpMessageHandler handler = new HttpClientHandler()
            // {
            //     CookieContainer = cookieJar
            // };
            //
            // HttpClient client = new(handler);
            // client.Timeout = TimeSpan.FromMinutes(15);
            // client.DefaultRequestHeaders.Add(DotYouHeaderNames.FileSystemTypeHeader, Enum.GetName(fileSystemType));
            // client.BaseAddress = new Uri($"https://{DnsConfigurationSet.PrefixApi}.{this._identity}");
            // return client;
        }
    }
}