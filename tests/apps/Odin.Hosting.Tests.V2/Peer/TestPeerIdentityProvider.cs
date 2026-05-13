#nullable enable
using Microsoft.AspNetCore.Http;
using Odin.Services.Authentication.Peer;

namespace Odin.Hosting.Tests.V2.Peer;

/// <summary>
/// Test-side impl of <see cref="ITestPeerIdentityProvider"/>. Reads the
/// <see cref="TestPeerHttpClientFactory.TestPeerIdentityHeader"/> set by outbound test peer
/// calls; absent header returns null, letting the production session-validate-callback path run.
/// Registered in <see cref="Hosting.OdinHost"/>'s root container — production never sees this type.
/// </summary>
internal sealed class TestPeerIdentityProvider : ITestPeerIdentityProvider
{
    public string? TryReadIdentityFrom(HttpRequest request)
    {
        var value = request.Headers[TestPeerHttpClientFactory.TestPeerIdentityHeader].ToString();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }
}
