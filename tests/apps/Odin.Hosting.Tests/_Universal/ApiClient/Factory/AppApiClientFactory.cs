using System;
using System.Net.Http;
using Odin.Core;
using Odin.Core.Identity;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Base;
using Odin.Core.Storage;
using Odin.Hosting.Authentication.YouAuth;
using Odin.Hosting.Controllers.ClientToken.App;
using Odin.Hosting.Tests.AppAPI.ApiClient.Base;

namespace Odin.Hosting.Tests._Universal.ApiClient.Factory;

public class AppApiClientFactory : IApiClientFactory
{
    private readonly ClientAuthenticationToken _token;
    private readonly byte[] _sharedSecret;

    public AppApiClientFactory(ClientAuthenticationToken token, byte[] sharedSecret)
    {
        _token = token;
        _sharedSecret = sharedSecret;
    }

    public HttpClient CreateHttpClient(OdinId identity, out SensitiveByteArray sharedSecret, FileSystemType fileSystemType = FileSystemType.Standard)
    {
        var client = WebScaffold.HttpClientFactory.CreateClient(
            $"{nameof(AppApiClientFactory)}:{identity}:{WebScaffold.HttpsPort}",
            config => config.MessageHandlerChain.Add(inner => new SharedSecretGetRequestHandler(inner)));

        var token = _token;
        sharedSecret = _sharedSecret.ToSensitiveByteArray();

        //
        // SEB:NOTE below is a hack to make SharedSecretGetRequestHandler work without instance data.
        // DO NOT do this in production code!
        //
        {
            var cookieValue = $"{YouAuthConstants.AppCookieName}={token}";
            client.DefaultRequestHeaders.Add("Cookie", cookieValue);
            client.DefaultRequestHeaders.Add("X-HACK-COOKIE", cookieValue);
            client.DefaultRequestHeaders.Add("X-HACK-SHARED-SECRET", Convert.ToBase64String(_sharedSecret));
        }

        client.DefaultRequestHeaders.Add(OdinHeaderNames.FileSystemTypeHeader, Enum.GetName(fileSystemType));
        client.Timeout = TimeSpan.FromMinutes(15);

        client.BaseAddress = new Uri($"https://{identity}:{WebScaffold.HttpsPort}{AppApiPathConstantsV1.BasePathV1}");
        return client;
    }
}