#nullable enable
using System;
using System.Net.Http;
using System.Net.Http.Headers;
using Odin.Core;
using Odin.Core.Identity;
using Odin.Core.Storage;
using Odin.Hosting.Tests;
using Odin.Hosting.Tests._Universal.ApiClient.Factory;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Base;

namespace Odin.Hosting.Tests.V2.Hosting;

/// <summary>
/// V2-shaped <see cref="IApiClientFactory"/> that produces HttpClients backed by an in-process
/// <see cref="OdinHost"/>. Mirrors the auth-header conventions of <c>ApiClientFactoryV2</c> so
/// existing V2 client classes (<c>AuthV2Client</c>, <c>DriveReaderV2Client</c>, etc.) work unchanged.
/// </summary>
public sealed class InProcessApiClientFactory : IApiClientFactory
{
    private readonly OdinHost _host;
    private readonly string _cookieName;
    private readonly ClientAuthenticationToken _token;
    private readonly SensitiveByteArray? _sharedSecret;

    public InProcessApiClientFactory(
        OdinHost host,
        string cookieName,
        ClientAuthenticationToken token,
        SensitiveByteArray? sharedSecret = null)
    {
        _host = host;
        _cookieName = cookieName;
        _token = token;
        _sharedSecret = sharedSecret;
    }

    /// <remarks>
    /// Nominally non-nullable per <see cref="IApiClientFactory"/>, but the interface predates nullable
    /// reference types and the upstream <c>ApiClientFactoryV2</c> also returns null for unauthenticated
    /// callers. We mirror that.
    /// </remarks>
    public SensitiveByteArray SharedSecret => _sharedSecret!;

    public HttpClient CreateHttpClient(
        OdinId identity,
        out SensitiveByteArray sharedSecret,
        FileSystemType fileSystemType = FileSystemType.Standard)
    {
        HttpMessageHandler handler = _host.Server.CreateHandler();
        if (_sharedSecret != null)
        {
            handler = new SharedSecretGetRequestHandler(handler);
        }

        var client = new HttpClient(handler);

        var cookieValue = $"{_cookieName}={_token}";
        client.DefaultRequestHeaders.Add("Cookie", cookieValue);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _token.ToPortableBytes64());
        client.DefaultRequestHeaders.Add("X-HACK-COOKIE", cookieValue);
        if (_sharedSecret != null)
        {
            client.DefaultRequestHeaders.Add("X-HACK-SHARED-SECRET", Convert.ToBase64String(_sharedSecret.GetKey()));
        }

        client.DefaultRequestHeaders.Add(OdinHeaderNames.FileSystemTypeHeader, Enum.GetName(typeof(FileSystemType), fileSystemType));
        client.BaseAddress = new Uri($"https://{identity}/");
        sharedSecret = _sharedSecret!;
        return client;
    }
}
