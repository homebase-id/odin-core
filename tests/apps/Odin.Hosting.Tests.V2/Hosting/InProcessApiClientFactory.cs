#nullable enable
using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.TestHost;
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
    // Process-wide cache of the inner handler produced by TestServer.CreateHandler(). The handler
    // just routes inbound requests back into the TestServer, so reusing one across every HttpClient
    // built against the same server is correct — and avoids allocating a fresh delegating chain on
    // every Refit interface construction (every OwnerAdmin call would otherwise create one). Keyed
    // by TestServer reference so distinct OdinHost instances each get their own. ConditionalWeakTable
    // would be cleaner but requires a reference-type key; TestServer qualifies, so we use it.
    private static readonly ConditionalWeakTable<TestServer, HttpMessageHandler> _innerHandlerCache = new();

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
        // Reuse the inner TestServer handler; wrap in per-client delegating handlers and use
        // disposeHandler:false so HttpClient.Dispose doesn't tear down the shared inner.
        var innerHandler = _innerHandlerCache.GetValue(_host.Server, s => s.CreateHandler());
        HttpMessageHandler handler = new NonDisposingPassthroughHandler(innerHandler);
        if (_sharedSecret != null)
        {
            handler = new SharedSecretGetRequestHandler(handler);
        }
        handler = new V1PathNormalizingHandler(handler);

        var client = new HttpClient(handler, disposeHandler: true);

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
    /// Passes the request straight through to the cached inner TestServer handler. Exists so the
    /// outer per-client delegating chain can be disposed by <see cref="HttpClient.Dispose()"/>
    /// without taking the shared inner handler with it (<see cref="DelegatingHandler"/> disposes
    /// its inner by default).
    /// </summary>
    private sealed class NonDisposingPassthroughHandler(HttpMessageHandler inner) : DelegatingHandler(inner)
    {
        protected override void Dispose(bool disposing)
        {
            // Intentionally do NOT propagate disposal to the inner handler — it's shared across
            // all clients on this TestServer. base.Dispose with disposing=false skips inner.Dispose.
            base.Dispose(disposing: false);
        }
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
