#nullable enable
using Microsoft.AspNetCore.Http;

namespace Odin.Services.Authentication.Peer;

/// <summary>
/// Optional DI hook for test frameworks: when an implementation is registered, the production
/// peer-incoming authentication handler (<c>PeerCapiAuthenticationHandler</c>) consults it before
/// running the production session-validate-callback dance. Returning a non-null identity
/// short-circuits authentication with that identity as the peer.
/// </summary>
/// <remarks>
/// Production code never registers an implementation, so the handler's <c>GetService</c> call
/// returns <c>null</c> and the production path runs unchanged. This replaces an earlier
/// config-flag-driven bypass and keeps test-specific knowledge of header names out of production.
/// </remarks>
public interface ITestPeerIdentityProvider
{
    /// <summary>
    /// Returns the peer's identity domain (e.g. <c>frodo.dotyou.cloud</c>) if the request carries
    /// trusted test-mode identification; otherwise <c>null</c> to let the production path run.
    /// </summary>
    string? TryReadIdentityFrom(HttpRequest request);
}
