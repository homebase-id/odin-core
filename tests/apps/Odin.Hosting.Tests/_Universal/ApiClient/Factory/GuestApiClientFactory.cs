#nullable enable
using System;
using System.Net.Http;
using Odin.Core;
using Odin.Core.Identity;
using Odin.Services.Authentication.YouAuth;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Base;
using Odin.Core.Storage;
using Odin.Hosting.Controllers.ClientToken.Guest;
using Odin.Hosting.Tests.AppAPI.ApiClient.Base;

namespace Odin.Hosting.Tests._Universal.ApiClient.Factory;

public class GuestApiClientFactory : IApiClientFactory
{
    private readonly ClientAuthenticationToken? _token;
    private readonly byte[]? _sharedSecret;

    public GuestApiClientFactory() : this(null, null)
    {
    }

    public GuestApiClientFactory(ClientAuthenticationToken? token, byte[]? sharedSecret)
    {
        _token = token;
        _sharedSecret = sharedSecret;
    }

    public HttpClient CreateHttpClient(OdinId identity, out SensitiveByteArray sharedSecret, FileSystemType fileSystemType = FileSystemType.Standard)
    {
        var client = WebScaffold.CreateHttpClient<AppApiClientBase>();
        //
        // SEB:NOTE below is a hack to make SharedSecretGetRequestHandler work without instance data.
        // DO NOT do this in production code!
        //
        {
            if (_token != null && _sharedSecret != null)
            {
                var cookieValue = $"{YouAuthDefaults.XTokenCookieName}={_token}";
                client.DefaultRequestHeaders.Add("Cookie", cookieValue);
                client.DefaultRequestHeaders.Add("X-HACK-COOKIE", cookieValue);
                client.DefaultRequestHeaders.Add("X-HACK-SHARED-SECRET", Convert.ToBase64String(_sharedSecret));
            }
        }

        client.DefaultRequestHeaders.Add(OdinHeaderNames.FileSystemTypeHeader, Enum.GetName(fileSystemType));
        client.Timeout = TimeSpan.FromMinutes(15);

        client.BaseAddress = new Uri($"https://{identity}:{WebScaffold.HttpsPort}{GuestApiPathConstants.BasePathV1}");

        sharedSecret = _sharedSecret == null ? new SensitiveByteArray(Guid.Empty.ToByteArray()) : _sharedSecret.ToSensitiveByteArray();
        return client;
    }
}