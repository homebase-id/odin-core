#nullable enable
using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Hosting.Tests.V2.Api;

namespace Odin.Hosting.Tests.V2.Auth;

/// <summary>
/// The owner's password nonce is used once.  The owner console makes two password proofs in parallel when it takes
/// over the recovery phrase; a browser that answered both nonce requests with one response gave them the same
/// nonce, and the server refused the second proof ("record is null").
/// </summary>
[TestFixture]
public class OwnerNonceTests : V2Fixture
{
    [Test]
    public async Task TheNonceIsNeverCached()
    {
        using var client = new HttpClient(Host.Server.CreateHandler())
        {
            BaseAddress = new Uri($"https://{Identities.Frodo}/")
        };

        using var response = await client.GetAsync("api/owner/v1/authentication/nonce");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var cacheControl = response.Headers.CacheControl;
        Assert.That(cacheControl?.NoStore, Is.True,
            $"Cache-Control was '{cacheControl}': a cached nonce gives two parallel proofs the same one");
    }
}
