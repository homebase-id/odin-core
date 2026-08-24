using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using Odin.Core.Exceptions;
using Odin.Core.Http;
using Odin.Services.Configuration;
using Odin.Services.Email.Dkim;
using Odin.Services.Email.Mailbox;

namespace Odin.Services.Tests.Email.Mailbox;

// Wire-shape tests over a scripted HTTP stub - the CI-runnable half of the
// provider's coverage (the docker-gated StalwartMailboxProviderTests exercise the
// same calls against a real Stalwart). Payload shapes per
// docs/stalwart-admin-api-notes.md.
public class StalwartMailboxProviderWireTests
{
    private const string Domain = "frodo.example.test";

    private readonly List<(string url, JsonNode? body)> _requests = [];
    private readonly Queue<string> _responses = new();

    private StalwartMailboxProvider CreateProvider()
    {
        _requests.Clear();
        _responses.Clear();

        var handler = new ScriptedHandler(request =>
        {
            var body = request.Content == null ? null : JsonNode.Parse(request.Content.ReadAsStringAsync().Result);
            _requests.Add((request.RequestUri!.PathAndQuery, body));

            if (request.RequestUri!.PathAndQuery == "/jmap/session")
            {
                return """{"primaryAccounts":{"urn:stalwart:jmap":"dadmin"}}""";
            }
            return _responses.Count > 0 ? _responses.Dequeue() : """{"methodResponses":[["error",{"type":"unscripted"},"0"]]}""";
        });

        var httpClientFactory = new Mock<IDynamicHttpClientFactory>();
        httpClientFactory
            .Setup(x => x.CreateClient(It.IsAny<string>(), It.IsAny<Action<ClientHandlerConfig>?>()))
            .Returns(() => new HttpClient(handler));

        return new StalwartMailboxProvider(
            new Mock<ILogger<StalwartMailboxProvider>>().Object,
            httpClientFactory.Object,
            new OdinConfiguration
            {
                Email = new OdinConfiguration.EmailSection
                {
                    Stalwart = new OdinConfiguration.StalwartSection
                    {
                        BaseUrl = "http://stalwart.test",
                        AdminUsername = "admin",
                        AdminPassword = "secret",
                    },
                },
            });
    }

    private static string GetResponse(string method, string listJson) =>
        """{"methodResponses":[["METHOD",{"accountId":"dadmin","list":LIST,"notFound":[]},"0"]]}"""
            .Replace("METHOD", method).Replace("LIST", listJson);

    private static string SetResponse(string method, string createdId = "newid") =>
        """{"methodResponses":[["METHOD",{"accountId":"dadmin","created":{"c1":{"id":"ID"}}},"0"]]}"""
            .Replace("METHOD", method).Replace("ID", createdId);

    private JsonNode? MethodArgs(int requestIndex) => _requests[requestIndex].body?["methodCalls"]?[0]?[1];
    private string MethodName(int requestIndex) => _requests[requestIndex].body?["methodCalls"]?[0]?[0]?.GetValue<string>() ?? "";

    //

    [Test]
    public async Task CreateMailboxIsIdempotent_ExistingAccountMeansNoCreateCall()
    {
        var provider = CreateProvider();
        _responses.Enqueue(GetResponse("x:Domain/get", """[{"id":"d1","name":"frodo.example.test"}]"""));
        _responses.Enqueue(GetResponse("x:Account/get", """[{"id":"a1","name":"frodo","domainId":"d1"}]"""));

        await provider.CreateMailboxAsync(Domain, $"frodo@{Domain}");

        Assert.That(_requests.Select(r => r.url).Count(u => u == "/jmap"), Is.EqualTo(2), "two lookups, zero writes");
    }

    [Test]
    public async Task CreateMailboxCreatesUserVariantWithServerSetFieldsOmitted()
    {
        var provider = CreateProvider();
        _responses.Enqueue(GetResponse("x:Domain/get", "[]"));
        _responses.Enqueue(SetResponse("x:Domain/set", "d1"));
        _responses.Enqueue(GetResponse("x:Account/get", "[]"));
        _responses.Enqueue(SetResponse("x:Account/set", "a1"));

        await provider.CreateMailboxAsync(Domain, $"frodo@{Domain}");

        var accountCreate = MethodArgs(4)?["create"]?["c1"]!;
        Assert.That(MethodName(4), Is.EqualTo("x:Account/set"));
        Assert.That(accountCreate["@type"]!.GetValue<string>(), Is.EqualTo("User"));
        Assert.That(accountCreate["name"]!.GetValue<string>(), Is.EqualTo("frodo"));
        Assert.That(accountCreate["domainId"]!.GetValue<string>(), Is.EqualTo("d1"));
        Assert.That(accountCreate["emailAddress"], Is.Null, "serverSet - derived from name@domain");
    }

    [Test]
    public async Task AliasesSerializeAsNumericKeyedMap()
    {
        var provider = CreateProvider();
        _responses.Enqueue(GetResponse("x:Domain/get", """[{"id":"d1","name":"frodo.example.test"}]"""));
        _responses.Enqueue(GetResponse("x:Domain/get", """[{"id":"d1","name":"frodo.example.test"}]"""));
        _responses.Enqueue(GetResponse("x:Account/get", """[{"id":"a1","name":"frodo","domainId":"d1"}]"""));
        _responses.Enqueue("""{"methodResponses":[["x:Account/set",{"accountId":"dadmin","updated":{"a1":null}},"0"]]}""");

        await provider.SetAliasesAsync(Domain, ["mail", "hello"]);

        var aliases = (JsonObject)MethodArgs(4)!["update"]!["a1"]!["aliases"]!;
        Assert.That(aliases.Select(kv => kv.Key), Is.EqualTo(new[] { "0", "1" }), "registry List<T> = numeric string keys");
        Assert.That(aliases["0"]!["name"]!.GetValue<string>(), Is.EqualTo("mail"));
        Assert.That(aliases["0"]!["domainId"]!.GetValue<string>(), Is.EqualTo("d1"));
        Assert.That(aliases["0"]!["enabled"]!.GetValue<bool>(), Is.True);
    }

    [Test]
    public async Task DkimPrivateKeyTravelsAsSecretTextVariantInPem()
    {
        var provider = CreateProvider();
        _responses.Enqueue(GetResponse("x:Domain/get", """[{"id":"d1","name":"frodo.example.test"}]"""));
        _responses.Enqueue(GetResponse("x:DkimSignature/get", "[]"));
        _responses.Enqueue(SetResponse("x:DkimSignature/set", "k1"));

        var key = DkimKeyGenerator.GenerateEd25519Key("s1");
        await provider.SetDkimKeyAsync(Domain, key);

        var create = MethodArgs(3)?["create"]?["c1"]!;
        Assert.That(create["@type"]!.GetValue<string>(), Is.EqualTo("Dkim1Ed25519Sha256"));
        Assert.That(create["selector"]!.GetValue<string>(), Is.EqualTo("s1"));
        Assert.That(create["privateKey"]!["@type"]!.GetValue<string>(), Is.EqualTo("Text"));
        Assert.That(create["privateKey"]!["secret"]!.GetValue<string>(), Does.StartWith("-----BEGIN PRIVATE KEY-----"));
    }

    [Test]
    public async Task AppPasswordReturnsTheServerGeneratedSecret()
    {
        var provider = CreateProvider();
        _responses.Enqueue(GetResponse("x:Domain/get", """[{"id":"d1","name":"frodo.example.test"}]"""));
        _responses.Enqueue(GetResponse("x:Account/get", """[{"id":"a1","name":"frodo","domainId":"d1"}]"""));
        _responses.Enqueue("""{"methodResponses":[["x:AppPassword/set",{"accountId":"a1","created":{"c1":{"id":"p1","secret":"app_generated"}}},"0"]]}""");

        var provision = await provider.ProvisionAppPasswordAsync(Domain, $"frodo@{Domain}", "Thunderbird");

        Assert.That(provision.Secret, Is.EqualTo("app_generated"));
        Assert.That(provision.Id, Is.EqualTo("p1"));
        Assert.That(MethodArgs(3)?["accountId"]!.GetValue<string>(), Is.EqualTo("a1"), "child objects use the USER's account context");
    }

    /// <summary>
    /// The live server answers a destroy of an unknown id with notDestroyed/notFound rather than
    /// failing the request. Revoke is contractually idempotent, so that one reason is success.
    /// </summary>
    [Test]
    public async Task RevokingAnAlreadyGoneAppPasswordSucceeds()
    {
        var provider = CreateProvider();
        _responses.Enqueue(GetResponse("x:Domain/get", """[{"id":"d1","name":"frodo.example.test"}]"""));
        _responses.Enqueue(GetResponse("x:Account/get", """[{"id":"a1","name":"frodo","domainId":"d1"}]"""));
        _responses.Enqueue("""{"methodResponses":[["x:AppPassword/set",{"accountId":"a1","notDestroyed":{"p1":{"type":"notFound"}}},"0"]]}""");

        Assert.DoesNotThrowAsync(() => provider.RevokeAppPasswordAsync(Domain, "p1"));
        await Task.CompletedTask;
    }

    /// <summary>Any other destroy failure is a real failure and must not be swallowed.</summary>
    [Test]
    public void RevokingWithAnUnexpectedFailureThrows()
    {
        var provider = CreateProvider();
        _responses.Enqueue(GetResponse("x:Domain/get", """[{"id":"d1","name":"frodo.example.test"}]"""));
        _responses.Enqueue(GetResponse("x:Account/get", """[{"id":"a1","name":"frodo","domainId":"d1"}]"""));
        _responses.Enqueue("""{"methodResponses":[["x:AppPassword/set",{"accountId":"a1","notDestroyed":{"p1":{"type":"forbidden"}}},"0"]]}""");

        Assert.ThrowsAsync<OdinSystemException>(() => provider.RevokeAppPasswordAsync(Domain, "p1"));
    }

    [Test]
    public async Task UsageReadsTheAccountsDiskQuota()
    {
        var provider = CreateProvider();
        _responses.Enqueue(GetResponse("x:Domain/get", """[{"id":"d1","name":"frodo.example.test"}]"""));
        _responses.Enqueue(GetResponse("x:Account/get",
            """[{"id":"a1","name":"frodo","domainId":"d1","@type":"User","usedDiskQuota":4096,"quotas":{"maxDiskQuota":10485760}}]"""));

        var usage = await provider.GetUsageAsync(Domain);

        Assert.That(usage, Is.Not.Null);
        Assert.That(usage!.UsedBytes, Is.EqualTo(4096));
        Assert.That(usage.QuotaBytes, Is.EqualTo(10485760));
    }

    /// <summary>An unreported quota is unlimited, not an error.</summary>
    [Test]
    public async Task UsageWithoutAQuotaReportsNullQuota()
    {
        var provider = CreateProvider();
        _responses.Enqueue(GetResponse("x:Domain/get", """[{"id":"d1","name":"frodo.example.test"}]"""));
        _responses.Enqueue(GetResponse("x:Account/get",
            """[{"id":"a1","name":"frodo","domainId":"d1","@type":"User","usedDiskQuota":0,"quotas":{}}]"""));

        var usage = await provider.GetUsageAsync(Domain);

        Assert.That(usage!.QuotaBytes, Is.Null);
    }

    /// <summary>A mail server that cannot answer must not take the status screen down.</summary>
    [Test]
    public async Task UsageDegradesWhenTheDomainIsUnknown()
    {
        var provider = CreateProvider();
        _responses.Enqueue(GetResponse("x:Domain/get", "[]"));

        Assert.That(await provider.GetUsageAsync(Domain), Is.Null);
    }

    [Test]
    public void FailedSetsThrowWithTheServersDiagnostics()
    {
        var provider = CreateProvider();
        _responses.Enqueue(GetResponse("x:Domain/get", "[]"));
        _responses.Enqueue(
            """{"methodResponses":[["x:Domain/set",{"accountId":"dadmin","notCreated":{"c1":{"type":"invalidProperties","description":"boom"}}},"0"]]}""");

        var exception = Assert.ThrowsAsync<OdinSystemException>(() => provider.CreateMailboxAsync(Domain, $"frodo@{Domain}"));
        Assert.That(exception!.Message, Does.Contain("notCreated").And.Contain("boom"));
    }

    //

    private class ScriptedHandler(Func<HttpRequestMessage, string> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responder(request)),
            });
        }
    }
}
