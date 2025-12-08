#nullable enable
using System;
using System.Net.Http;
using Odin.Core;
using Odin.Core.Identity;
using Odin.Services.Authentication.YouAuth;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Base;
using Odin.Core.Storage;
using Odin.Hosting.Authentication.YouAuth;
using Odin.Hosting.Controllers.ClientToken.Guest;

namespace Odin.Hosting.Tests._Universal.ApiClient.Factory;

public class GuestApiClientFactory(ClientAuthenticationToken? token, byte[]? secret) : IApiClientFactory
{
    public SensitiveByteArray? SharedSecret { get; } = secret?.ToSensitiveByteArray();
    
    public GuestApiClientFactory() : this(null, null)
    {
    }

    public HttpClient CreateHttpClient(OdinId identity, out SensitiveByteArray sharedSecret, FileSystemType fileSystemType = FileSystemType.Standard)
    {
        var client = WebScaffold.HttpClientFactory.CreateClient(
            $"{nameof(GuestApiClientFactory)}:{identity}:{WebScaffold.HttpsPort}",
            config => config.MessageHandlerChain.Add(inner => new SharedSecretGetRequestHandler(inner)));

        //
        // SEB:NOTE below is a hack to make SharedSecretGetRequestHandler work without instance data.
        // DO NOT do this in production code!
        //
        {
            if (token != null && secret != null)
            {
                var cookieValue = $"{YouAuthDefaults.XTokenCookieName}={token}";
                client.DefaultRequestHeaders.Add("Cookie", cookieValue);
                client.DefaultRequestHeaders.Add("X-HACK-COOKIE", cookieValue);
                client.DefaultRequestHeaders.Add("X-HACK-SHARED-SECRET", Convert.ToBase64String(secret));
            }
        }

        client.DefaultRequestHeaders.Add(OdinHeaderNames.FileSystemTypeHeader, Enum.GetName(fileSystemType));
        client.Timeout = TimeSpan.FromMinutes(15);

        client.BaseAddress = new Uri($"https://{identity}:{WebScaffold.HttpsPort}{GuestApiPathConstantsV1.BasePathV1}");

        sharedSecret = secret == null ? new SensitiveByteArray(Guid.Empty.ToByteArray()) : secret.ToSensitiveByteArray();
        return client;
    }
}