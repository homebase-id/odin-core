#nullable enable
using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Odin.Core;
using Odin.Core.Identity;
using Odin.Core.Storage;
using Odin.Hosting.Tests;
using Odin.Hosting.Tests._Universal.ApiClient.Factory;
using Odin.Services.Authentication.Owner;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Base;

namespace Odin.Hosting.Tests.V2.Hosting;

/// <summary>
/// V2-shaped <see cref="IApiClientFactory"/> that produces HttpClients backed by an in-process
/// <see cref="OdinHost"/>. Mirrors the auth-header conventions of <c>ApiClientFactoryV2</c> so
/// existing V2 client classes (<c>AuthV2Client</c>, <c>DriveReaderV2Client</c>, etc.) work unchanged.
/// </summary>
/// <remarks>
/// <para>
/// The clients produced here serve <b>both</b> V2 endpoints (whose Refit interfaces use absolute
/// <c>/api/v2/...</c> paths) and V1 admin endpoints (whose Refit interfaces use paths relative to
/// <c>/api/owner/v1</c>) without two separate factories. The trick is <see cref="V1PathNormalizingHandler"/>,
/// which rewrites any outgoing request whose path doesn't start with <c>/api/</c> by prefixing
/// <see cref="OwnerApiPathConstants.BasePathV1"/>. Refit's URL composition (raw string concatenation)
/// otherwise produces broken paths for V1-relative interfaces against a root <c>BaseAddress</c>.
/// </para>
/// </remarks>
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

    /// <summary>
    /// Non-null shared secret; throws <see cref="InvalidOperationException"/> if this factory was
    /// constructed without one. <see cref="IApiClientFactory"/>'s signature predates nullable
    /// reference types — the upstream <c>ApiClientFactoryV2</c> silently returns null here, which
    /// causes downstream NREs in confusing places. Throwing locally is the lesser evil.
    /// </summary>
    public SensitiveByteArray SharedSecret =>
        _sharedSecret ?? throw new InvalidOperationException(
            "InProcessApiClientFactory was constructed without a shared secret — callers asking " +
            "for it haven't authenticated.");

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
        handler = new V1PathNormalizingHandler(handler);

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
        sharedSecret = _sharedSecret ?? throw new InvalidOperationException(
            "InProcessApiClientFactory.CreateHttpClient(out ...) requires a shared secret; this factory " +
            "was constructed without one. IApiClientFactory's signature predates nullable references — " +
            "silently smuggling a null out-param causes confusing downstream NREs.");
        return client;
    }

    /// <summary>
    /// Rewrites outgoing request URIs so that V1 admin Refit interfaces (which declare paths like
    /// <c>/circles/definitions/create</c> relative to <c>/api/owner/v1</c>) work against a root
    /// <c>BaseAddress</c>. Anything that already starts with <c>/api/</c> (V2 endpoints, V1 owner
    /// interfaces that pre-include the prefix) passes through unchanged.
    /// </summary>
    private sealed class V1PathNormalizingHandler(HttpMessageHandler inner) : DelegatingHandler(inner)
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.RequestUri is { } uri)
            {
                var path = uri.AbsolutePath;
                while (path.StartsWith("//", StringComparison.Ordinal))
                {
                    path = path[1..];
                }

                if (!path.StartsWith("/api/", StringComparison.Ordinal))
                {
                    path = OwnerApiPathConstants.BasePathV1 + path;
                }

                if (path != uri.AbsolutePath)
                {
                    request.RequestUri = new UriBuilder(uri) { Path = path }.Uri;
                }
            }

            return base.SendAsync(request, ct);
        }
    }
}
