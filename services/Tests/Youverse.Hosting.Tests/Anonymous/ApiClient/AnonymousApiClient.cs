using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Youverse.Core;
using Youverse.Core.Identity;
using Youverse.Core.Services.Authorization.ExchangeGrants;
using Youverse.Core.Services.Base;
using Youverse.Core.Storage;
using Youverse.Hosting.Tests.AppAPI.Utils;
using Youverse.Hosting.Tests.OwnerApi.ApiClient;
using Youverse.Hosting.Tests.OwnerApi.Utils;

namespace Youverse.Hosting.Tests.Anonymous.ApiClient
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
            var cookieJar = new CookieContainer();
            HttpMessageHandler handler = new HttpClientHandler()
            {
                CookieContainer = cookieJar
            };

            HttpClient client = new(handler);
            client.Timeout = TimeSpan.FromMinutes(15);
            client.DefaultRequestHeaders.Add(DotYouHeaderNames.FileSystemTypeHeader, Enum.GetName(fileSystemType));
            client.BaseAddress = new Uri($"https://{this._identity}");
            return client;
        }
    }
}