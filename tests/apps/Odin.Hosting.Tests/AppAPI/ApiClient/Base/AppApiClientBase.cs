using System;
using System.Net.Http;
using Odin.Core.Identity;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Base;
using Odin.Core.Storage;
using Odin.Hosting.Authentication.YouAuth;
using Odin.Hosting.Tests.OwnerApi.Utils;

namespace Odin.Hosting.Tests.AppAPI.ApiClient.Base
{
    public abstract class AppApiClientBase
    {
        private readonly OwnerApiTestUtils _ownerApi;

        public AppApiClientBase(OwnerApiTestUtils ownerApi)
        {
            _ownerApi = ownerApi;
        }
        
        protected HttpClient CreateAppApiHttpClient(OdinId identity, ClientAuthenticationToken token, byte[] sharedSecret, FileSystemType fileSystemType)
        {
            var client = WebScaffold.HttpClientFactory.CreateClient(
                $"{nameof(AppApiClientBase)}:{identity}:{WebScaffold.HttpsPort}",
                config => config.MessageHandlerChain.Add(inner => new SharedSecretGetRequestHandler(inner)));

            //
            // SEB:NOTE below is a hack to make SharedSecretGetRequestHandler work without instance data.
            // DO NOT do this in production code!
            //
            {
                var cookieValue = $"{OdinHeaderNames.AppCookie}={token}";
                client.DefaultRequestHeaders.Add("Cookie", cookieValue);
                client.DefaultRequestHeaders.Add("X-HACK-COOKIE", cookieValue);
                client.DefaultRequestHeaders.Add("X-HACK-SHARED-SECRET", Convert.ToBase64String(sharedSecret));
            }
            
            client.DefaultRequestHeaders.Add(OdinHeaderNames.FileSystemTypeHeader, Enum.GetName(fileSystemType));
            client.Timeout = TimeSpan.FromMinutes(15);
            
            client.BaseAddress = new Uri($"https://{identity}:{WebScaffold.HttpsPort}");
            return client;
            
        }

        protected HttpClient CreateAppApiHttpClient(TestAppContext appTestContext, FileSystemType fileSystemType = FileSystemType.Standard)
        {
            return CreateAppApiHttpClient(appTestContext.Identity, appTestContext.ClientAuthenticationToken, appTestContext.SharedSecret, fileSystemType);
        }

        protected HttpClient CreateAppApiHttpClient(AppClientToken token, FileSystemType fileSystemType = FileSystemType.Standard)
        {
            return CreateAppApiHttpClient(token.OdinId, token.ClientAuthToken, token.SharedSecret, fileSystemType);
        }

    }
}