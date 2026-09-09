using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Odin.Core.Storage.Cache;
using Odin.Services.Certificate;

namespace Odin.Hosting.Tests.Middleware;

#nullable enable

/// <summary>
/// Pins, through the real middleware pipeline, that an HTTP-01 challenge for the mta-sts host
/// reaches CertesAcmeMiddleware when tenant mail is enabled.
/// </summary>
/// <remarks>
/// The unit test on MtaStsMiddleware alone cannot pin this. It was the ORDER of registration in
/// Startup - MtaStsMiddleware before CertesAcmeMiddleware - that decided whether the challenge
/// was answered or 404'd, and that order is only exercised by driving a request through the
/// host. Before the fix this test fails with 404: Let's Encrypt fetched the challenge and was
/// refused by our own middleware, so the mta-sts SAN could never validate in the only
/// configuration in which it is requested (2026-09-08).
/// </remarks>
public class AcmeChallengeOnMtaStsHostTest
{
    private const string MtaStsHost = "mta-sts.frodo.dotyou.cloud";

    private static readonly Dictionary<string, string?> TenantMailEnvironment = new()
    {
        ["Email__TenantMail__Enabled"] = "true",
        ["Email__TenantMail__MxNodes__0"] = "mx1.example.test",
        ["Email__TenantMail__SpfIncludeTarget"] = "_spf.example.test",
        ["Email__TenantMail__DmarcReportEmail"] = "dmarc@example.test",
        ["Email__TenantMail__TlsReportEmail"] = "tlsrpt@example.test",
    };

    private WebScaffold _scaffold = null!;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        // Tenant mail on: the only configuration in which the mta-sts SAN is ordered, and so the
        // only one in which its challenge must be answerable.
        foreach (var (key, value) in TenantMailEnvironment)
        {
            Environment.SetEnvironmentVariable(key, value);
        }

        _scaffold = new WebScaffold(nameof(AcmeChallengeOnMtaStsHostTest));
        _scaffold.RunBeforeAnyTests(testIdentities: new List<TestIdentity> { TestIdentities.Frodo });
    }

    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        _scaffold.RunAfterAnyTests();
        foreach (var key in TenantMailEnvironment.Keys)
        {
            Environment.SetEnvironmentVariable(key, null);
        }
    }

    //

    [Test]
    public async Task TheAcmeChallengeIsAnsweredOnTheMtaStsHost()
    {
        const string token = "pipeline-test-token";
        const string keyAuthorization = "pipeline-test-token.thumbprint";

        // What CertesAcme does when it asks the CA to validate: park the key authorization
        // where CertesAcmeMiddleware will look for it
        var tokenCache = _scaffold.Services.GetRequiredService<ISystemLevel2Cache<CertesAcme>>();
        await tokenCache.SetAsync(token, keyAuthorization, TimeSpan.FromMinutes(5));

        var response = await GetOnMtaStsHostAsync($"/.well-known/acme-challenge/{token}");
        var body = await response.Content.ReadAsStringAsync();

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK),
            "The ACME challenge must reach CertesAcmeMiddleware; MtaStsMiddleware must not answer it");
        Assert.That(body, Is.EqualTo(keyAuthorization));
    }

    [Test]
    public async Task TheMtaStsHostStillServesNothingElse()
    {
        // The exemption is for the challenge path only
        var response = await GetOnMtaStsHostAsync("/some/other/path");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    //

    // Plain HTTP, as HTTP-01 arrives. Addressed by Host header so that DNS for the mta-sts
    // subdomain is not a precondition of the test.
    private static async Task<HttpResponseMessage> GetOnMtaStsHostAsync(string path)
    {
        using var client = new HttpClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, $"http://127.0.0.1:{WebScaffold.HttpPort}{path}");
        request.Headers.Host = MtaStsHost;
        return await client.SendAsync(request);
    }
}
