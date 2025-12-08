#nullable enable
using System;
using System.Net.Http;
using Odin.Core;
using Odin.Core.Identity;
using Odin.Core.Storage;
using Odin.Hosting.Tests._Universal.ApiClient.Factory;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Base;

namespace Odin.Hosting.Tests._V2.ApiClient.Factory;

public class ApiClientFactoryV2(string cookieName, ClientAuthenticationToken token, byte[]? secret = null) : IApiClientFactory
{
    public SensitiveByteArray? SharedSecret { get; } = secret?.ToSensitiveByteArray();

    public HttpClient CreateHttpClient(OdinId identity, out SensitiveByteArray? sharedSecret,
        FileSystemType fileSystemType = FileSystemType.Standard)
    {
        HttpClient client;
        var name = $"{nameof(ApiClientFactoryV2)}:{identity}:{WebScaffold.HttpsPort}";

        if (secret == null)
        {
            client = WebScaffold.HttpClientFactory.CreateClient(name);
        }
        else
        {
            client = WebScaffold.HttpClientFactory.CreateClient(
                name,
                config => config.MessageHandlerChain.Add(inner => new SharedSecretGetRequestHandler(inner)));
        }

        //
        // SEB:NOTE below is a hack to make SharedSecretGetRequestHandler work without instance data.
        // DO NOT do this in production code!
        //
        {
            //TODO: this needs to send the client token type w/ the cooke
            var cookieValue = $"{cookieName}={token}";
            client.DefaultRequestHeaders.Add("Cookie", cookieValue);
            client.DefaultRequestHeaders.Add("X-HACK-COOKIE", cookieValue);
            if (secret != null)
            {
                client.DefaultRequestHeaders.Add("X-HACK-SHARED-SECRET", Convert.ToBase64String(secret));
            }
        }

        client.DefaultRequestHeaders.Add(OdinHeaderNames.FileSystemTypeHeader, Enum.GetName(typeof(FileSystemType), fileSystemType));
        client.Timeout = TimeSpan.FromMinutes(15);

        client.BaseAddress = new Uri($"https://{identity}:{WebScaffold.HttpsPort}");
        sharedSecret = secret?.ToSensitiveByteArray();

        return client;
    }
}