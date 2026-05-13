#nullable enable
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.TestHost;
using Odin.Core.Identity;
using Odin.Core.Storage;
using Odin.Services.Authentication.Owner;
using Odin.Services.Authorization.Capi;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Base;
using Odin.Services.Registry.Registration;
using Refit;

namespace Odin.Hosting.Tests.V2.Peer;

/// <summary>
/// Test replacement for the production <see cref="OdinHttpClientFactory"/>. Outbound peer requests
/// (Frodo → Sam) are routed back to the in-process <see cref="TestServer"/> via its
/// <see cref="TestServer.CreateHandler"/> instead of going over the wire. Authentication on the
/// receiving side is via the <see cref="TestPeerIdentityHeader"/> bypass in
/// <c>PeerCapiAuthenticationHandler</c>, not mTLS or the production CAPI session callback dance.
/// </summary>
internal sealed class TestPeerHttpClientFactory : IOdinHttpClientFactory
{
    /// <summary>
    /// Header carrying the calling identity for the test-only peer-auth bypass. Read by
    /// <c>PeerCapiAuthenticationHandler.HandleAuthenticateAsync</c> when
    /// <c>OdinConfiguration.Testing.EnableSyncHooks</c> is true.
    /// </summary>
    public const string TestPeerIdentityHeader = "X-Test-Peer-Identity";

    private readonly TestServerHolder _serverHolder;
    private readonly OdinIdentity _localIdentity;

    public TestPeerHttpClientFactory(TestServerHolder serverHolder, OdinIdentity localIdentity)
    {
        _serverHolder = serverHolder;
        _localIdentity = localIdentity;
    }

    public Task<T> CreateClientUsingAccessTokenAsync<T>(
        OdinId remoteOdinId,
        ClientAuthenticationToken clientAuthenticationToken,
        FileSystemType? fileSystemType = null)
        => Task.FromResult(BuildClient<T>(remoteOdinId, clientAuthenticationToken, fileSystemType, headers: null));

    public Task<T> CreateClientAsync<T>(
        OdinId remoteOdinId,
        FileSystemType? fileSystemType = null,
        Dictionary<string, string>? headers = null)
        => Task.FromResult(BuildClient<T>(remoteOdinId, clientAuthenticationToken: null, fileSystemType, headers));

    private T BuildClient<T>(
        OdinId remoteOdinId,
        ClientAuthenticationToken? clientAuthenticationToken,
        FileSystemType? fileSystemType,
        Dictionary<string, string>? headers)
    {
        var server = _serverHolder.Server
            ?? throw new InvalidOperationException(
                "TestServer has not been wired into TestServerHolder yet — OdinHost must populate it after host.StartAsync.");

        // Mirror the production factory's BaseAddress (capi.{remote}) so the multi-tenant middleware
        // resolves the recipient tenant via the well-known "capi" prefix.
        var client = new HttpClient(server.CreateHandler())
        {
            BaseAddress = new Uri($"https://{DnsConfigurationSet.PrefixCertApi}.{remoteOdinId}/"),
        };

        // Bypass header for PeerCapiAuthenticationHandler — see TestPeerIdentityHeader xmldoc above.
        client.DefaultRequestHeaders.Add(TestPeerIdentityHeader, _localIdentity.PrimaryDomain);

        // RedirectIfNotApexMiddleware redirects GETs to capi.* back to the apex unless
        // X-CAPI-Session is present. The auth bypass above runs before the value is parsed, so
        // the contents here don't matter — but the header has to exist.
        client.DefaultRequestHeaders.Add(ICapiCallbackSession.SessionHttpHeaderName,
            $"{_localIdentity.PrimaryDomain}~test-session");

        if (fileSystemType.HasValue)
        {
            client.DefaultRequestHeaders.Add(OdinHeaderNames.FileSystemTypeHeader, fileSystemType.Value.ToString());
        }

        if (clientAuthenticationToken != null)
        {
            client.DefaultRequestHeaders.Add(OdinHeaderNames.ClientAuthToken, clientAuthenticationToken.ToString());
        }

        if (headers != null)
        {
            foreach (var (key, value) in headers)
            {
                if (!client.DefaultRequestHeaders.TryGetValues(key, out _))
                {
                    client.DefaultRequestHeaders.Add(key, value);
                }
            }
        }

        return RestService.For<T>(client);
    }
}

/// <summary>
/// Holds the <see cref="TestServer"/> reference between two phases of host startup. The DI delegate
/// that builds <see cref="TestPeerHttpClientFactory"/> resolves this at request time; the
/// <see cref="Hosting.OdinHost"/> populates <see cref="Server"/> after <c>host.GetTestServer()</c>
/// is available, before any peer test runs.
/// </summary>
internal sealed class TestServerHolder
{
    public TestServer? Server { get; set; }
}
