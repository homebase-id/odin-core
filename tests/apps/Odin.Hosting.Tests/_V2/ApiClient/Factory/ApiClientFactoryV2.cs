using System;
using System.Net.Http;
using Odin.Core;
using Odin.Core.Identity;
using Odin.Core.Storage;
using Odin.Hosting.Tests._Universal.ApiClient.Factory;
using Odin.Services.Authentication.Owner;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Base;

namespace Odin.Hosting.Tests._V2.ApiClient.Factory;

public class ApiClientFactoryV2(string cookieName, ClientAuthenticationToken token, byte[] secret) : IApiClientFactory
{
    public HttpClient CreateHttpClient(OdinId identity, out SensitiveByteArray sharedSecret, FileSystemType fileSystemType = FileSystemType.Standard)
    {
        var client = WebScaffold.HttpClientFactory.CreateClient(
            $"{nameof(ApiClientFactoryV2)}:{identity}:{WebScaffold.HttpsPort}",
            config => config.MessageHandlerChain.Add(inner => new SharedSecretGetRequestHandler(inner)));

        //
        // SEB:NOTE below is a hack to make SharedSecretGetRequestHandler work without instance data.
        // DO NOT do this in production code!
        //
        {
            //TODO: this needs to send the client token type w/ the cooke
            var cookieValue = $"{cookieName}={token}";
            client.DefaultRequestHeaders.Add("Cookie", cookieValue);
            client.DefaultRequestHeaders.Add("X-HACK-COOKIE", cookieValue);
            client.DefaultRequestHeaders.Add("X-HACK-SHARED-SECRET", Convert.ToBase64String(secret));
        }

        client.DefaultRequestHeaders.Add(OdinHeaderNames.FileSystemTypeHeader, Enum.GetName(typeof(FileSystemType), fileSystemType));
        client.Timeout = TimeSpan.FromMinutes(15);

        client.BaseAddress = new Uri($"https://{identity}:{WebScaffold.HttpsPort}");
        sharedSecret = secret.ToSensitiveByteArray();
        return client;
    }
}