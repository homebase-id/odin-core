using System.Collections.Generic;
#if RUN_STALWART_TESTS
using System;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using Odin.Core.Cryptography.Pgp;
using Odin.Core.Exceptions;
using Odin.Core.Http;
using Odin.Services.Configuration;
using Odin.Services.Email.Dkim;
using Odin.Services.Email.Mailbox;

namespace Odin.Services.Tests.Email.Mailbox;

// The live half of the provider's verification (docs/stalwart-admin-api-notes.md):
// every IMailboxProvider operation against a REAL Stalwart in a container, with
// odin's real key material (OpenPgpKeyManagement, DkimKeyGenerator). Compiled only
// where docker is known available (RUN_STALWART_TESTS).
[TestFixture]
public class StalwartMailboxProviderTests
{
    private const string Domain = "frodo.example.test";
    private const string PrimaryAddress = "frodo@frodo.example.test";
    private const string AdminUser = "admin";
    private const string AdminPassword = "testadminpass";

    private IContainer _stalwart = null!;
    private string _baseUrl = "";
    private StalwartMailboxProvider _provider = null!;

    [OneTimeSetUp]
    public async Task StartStalwart()
    {
        // config.json skips bootstrap mode; recovery mode = management API only,
        // which is all the provider ever touches
        _stalwart = new ContainerBuilder()
            .WithImage("stalwartlabs/stalwart:v0.16")
            .WithResourceMapping("{\"@type\":\"RocksDb\",\"path\":\"/var/lib/stalwart/\"}"u8.ToArray(), "/etc/stalwart/config.json")
            .WithEnvironment("STALWART_RECOVERY_MODE", "1")
            .WithEnvironment("STALWART_RECOVERY_ADMIN", $"{AdminUser}:{AdminPassword}")
            .WithPortBinding(8080, assignRandomHostPort: true)
            .WithWaitStrategy(Wait.ForUnixContainer()
                .UntilHttpRequestIsSucceeded(r => r.ForPort(8080).ForPath("/healthz/live")))
            .Build();
        await _stalwart.StartAsync();
        _baseUrl = $"http://localhost:{_stalwart.GetMappedPublicPort(8080)}";

        var httpClientFactory = new Mock<IDynamicHttpClientFactory>();
        httpClientFactory
            .Setup(x => x.CreateClient(It.IsAny<string>(), It.IsAny<Action<ClientHandlerConfig>?>()))
            .Returns(() => new HttpClient());

        var configuration = new OdinConfiguration
        {
            Email = new OdinConfiguration.EmailSection
            {
                Stalwart = new OdinConfiguration.StalwartSection
                {
                    BaseUrl = _baseUrl,
                    AdminUsername = AdminUser,
                    AdminPassword = AdminPassword,
                },
            },
        };

        _provider = new StalwartMailboxProvider(
            new Mock<ILogger<StalwartMailboxProvider>>().Object,
            httpClientFactory.Object,
            configuration);
    }

    [OneTimeTearDown]
    public async Task StopStalwart()
    {
        await _stalwart.DisposeAsync();
    }

    // One ordered flow: the operations depend on each other exactly as activation does
    [Test]
    public async Task ItShouldRunTheFullMailboxLifecycleAgainstRealStalwart()
    {
        // --- create (idempotent) ---
        await _provider.CreateMailboxAsync(Domain, PrimaryAddress);
        await _provider.CreateMailboxAsync(Domain, PrimaryAddress); // second run must not duplicate

        var accounts = await RegistryGetAsync("x:Account");
        Assert.That(accounts.Count(a => a["name"]!.GetValue<string>() == "frodo"), Is.EqualTo(1));
        Assert.That(accounts.Single(a => a["name"]!.GetValue<string>() == "frodo")["emailAddress"]!.GetValue<string>(),
            Is.EqualTo(PrimaryAddress), "emailAddress derived from name@domain");

        // --- encryption-at-rest with odin's real OpenPGP packaging ---
        var material = OpenPgpKeyManagement.GenerateP384KeyMaterial(PrimaryAddress);
        await _provider.SetEncryptionKeyAsync(Domain, material.PublicCertificateArmored);

        var account = (await RegistryGetAsync("x:Account")).Single(a => a["name"]!.GetValue<string>() == "frodo");
        Assert.That(account["encryptionAtRest"]!["@type"]!.GetValue<string>(), Is.EqualTo("Aes256"));
        Assert.That(account["encryptionAtRest"]!["encryptOnAppend"]!.GetValue<bool>(), Is.True);

        // --- DKIM with odin's real generator, both selectors ---
        var keys = DkimKeyGenerator.GenerateKeys();
        foreach (var key in keys)
        {
            await _provider.SetDkimKeyAsync(Domain, key);
        }

        // Stalwart derives the public key from the installed private key; it must
        // equal OUR DNS p= value byte for byte - the PR-G read-back drift primitive
        var signatures = await RegistryGetAsync("x:DkimSignature");
        foreach (var key in keys)
        {
            var installed = signatures.Single(s => s["selector"]!.GetValue<string>() == key.Selector);
            Assert.That(installed["publicKey"]!.GetValue<string>().Trim(), Is.EqualTo(key.PublicKeyBase64),
                $"derived public key must match our p= value for {key.Selector}");
        }

        // --- DKIM rotation: same selector, fresh key, derived key changes ---
        var rotated = DkimKeyGenerator.GenerateEd25519Key("s1");
        await _provider.SetDkimKeyAsync(Domain, rotated);
        var afterRotation = (await RegistryGetAsync("x:DkimSignature")).Single(s => s["selector"]!.GetValue<string>() == "s1");
        Assert.That(afterRotation["publicKey"]!.GetValue<string>().Trim(), Is.EqualTo(rotated.PublicKeyBase64));
        Assert.That((await RegistryGetAsync("x:DkimSignature")).Count(s => s["selector"]!.GetValue<string>() == "s1"),
            Is.EqualTo(1), "rotation must update, not duplicate");

        // --- aliases (one mailbox, many names), re-set converges ---
        await _provider.SetAliasesAsync(Domain, ["mail", "hello"]);
        await _provider.SetAliasesAsync(Domain, ["only"]);
        account = (await RegistryGetAsync("x:Account")).Single(a => a["name"]!.GetValue<string>() == "frodo");
        var aliases = (account["aliases"] as JsonObject)!;
        Assert.That(aliases.Select(kv => kv.Value!["name"]!.GetValue<string>()), Is.EqualTo(new[] { "only" }));

        // --- app password: server-generated, returned once, fresh per call ---
        var provision = await _provider.ProvisionAppPasswordAsync(Domain, PrimaryAddress, "Thunderbird");
        Assert.That(provision.Secret, Is.Not.Empty);
        Assert.That(provision.Id, Is.Not.Empty, "the id is the only handle a revoke has");
        var second = await _provider.ProvisionAppPasswordAsync(Domain, PrimaryAddress, "FairEmail");
        Assert.That(second.Secret, Is.Not.EqualTo(provision.Secret));
        Assert.That(second.Id, Is.Not.EqualTo(provision.Id));

        // --- revoke: real, and idempotent the second time (Stalwart answers notFound) ---
        await _provider.RevokeAppPasswordAsync(Domain, provision.Id);
        Assert.DoesNotThrowAsync(() => _provider.RevokeAppPasswordAsync(Domain, provision.Id));

        // --- status: a live account reports its own state ---
        var status = await _provider.GetMailboxStatusAsync(Domain);
        Assert.That(status, Is.Not.Null, "a provisioned account reports its mailbox status");
        Assert.That(status!.UsedBytes, Is.GreaterThanOrEqualTo(0));
        Assert.That(status.InboxUnread, Is.Zero, "a fresh mailbox has nothing unread");
        Assert.That(status.QueuedOutbound, Is.Zero);

        // --- deletion ride-along: account + DKIM + domain, then a clean no-op ---
        await _provider.DeleteMailboxAsync(Domain);
        Assert.That(await RegistryGetAsync("x:Domain"), Is.Empty);
        Assert.That(await RegistryGetAsync("x:DkimSignature"), Is.Empty);
        Assert.That((await RegistryGetAsync("x:Account")).Any(a => a["name"]?.GetValue<string>() == "frodo"), Is.False);
        Assert.DoesNotThrowAsync(() => _provider.DeleteMailboxAsync(Domain)); // idempotent

        // --- and life goes on: re-activation after deletion works ---
        await _provider.CreateMailboxAsync(Domain, PrimaryAddress);
        Assert.That((await RegistryGetAsync("x:Domain")).Count, Is.EqualTo(1));
    }

    /// <summary>
    /// Stalwart mints DKIM keys of its own when a domain is created - selectors like
    /// "v1-rsa-20260826" - and we publish DNS only for our s1/s2. Left in place, every outbound
    /// message carried FOUR signatures, two of which no verifier could resolve; Gmail reported
    /// them as `dkim=permerror (no key for signature)` on real mail from a live tenant.
    ///
    /// The foreign key here is INSTALLED BY THE TEST rather than waited for, because this
    /// container runs with STALWART_RECOVERY_MODE=1 - management API only - which suppresses
    /// the background task that generates them. Verified: the real dev and bleeding instances
    /// on the same v0.16 image do generate them, and polling this container for ten seconds
    /// never produced any.
    ///
    /// So what is under test is OUR reconciliation - "remove every signature for this domain
    /// that is not one of ours" - which is the part we control. Reproducing Stalwart's own
    /// generation is neither possible here nor our behaviour to assert.
    /// </summary>
    [Test]
    public async Task ItShouldRemoveDkimKeysWeDidNotInstallAndKeepOurs()
    {
        await _provider.CreateMailboxAsync(Domain, PrimaryAddress);

        var ours = DkimKeyGenerator.GenerateKeys();
        foreach (var key in ours)
        {
            await _provider.SetDkimKeyAsync(Domain, key);
        }
        var ourSelectors = ours.Select(k => k.Selector).ToList();

        // Stand in for what Stalwart generates for itself, using its real selector shape.
        var foreign = DkimKeyGenerator.GenerateEd25519Key("v1-ed25519-20260826");
        await _provider.SetDkimKeyAsync(Domain, foreign);

        var before = (await RegistryGetAsync("x:DkimSignature"))
            .Select(x => x["selector"]!.GetValue<string>()).ToList();
        Assert.That(before, Does.Contain(foreign.Selector), "precondition: the foreign key is installed");

        await _provider.RemoveForeignDkimSignaturesAsync(Domain, ourSelectors);

        var after = (await RegistryGetAsync("x:DkimSignature"))
            .Select(x => x["selector"]!.GetValue<string>()).ToList();
        Assert.That(after, Is.EquivalentTo(ourSelectors),
            "only the selectors we publish DNS for may remain");

        // Activation is re-runnable, so a second pass must neither throw nor remove ours.
        await _provider.RemoveForeignDkimSignaturesAsync(Domain, ourSelectors);
        var again = (await RegistryGetAsync("x:DkimSignature"))
            .Select(x => x["selector"]!.GetValue<string>()).ToList();
        Assert.That(again, Is.EquivalentTo(ourSelectors));
    }

    [Test]
    public async Task ItShouldRefuseChildOperationsBeforeTheMailboxExists()
    {
        Assert.ThrowsAsync<OdinSystemException>(
            () => _provider.ProvisionAppPasswordAsync("nobody.example.test", "x@nobody.example.test", "label"));
        await Task.CompletedTask;
    }

    //
    // Read-back straight against Stalwart (bypassing the provider) so assertions
    // are independent of the code under test
    //

    private async Task<System.Collections.Generic.List<JsonNode>> RegistryGetAsync(string objectType)
    {
        using var client = new HttpClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/jmap");
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{AdminUser}:{AdminPassword}")));

        var session = JsonNode.Parse(await (await client.SendAsync(new HttpRequestMessage(HttpMethod.Get, $"{_baseUrl}/jmap/session")
        {
            Headers = { Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{AdminUser}:{AdminPassword}"))) }
        })).Content.ReadAsStringAsync())!;
        var accountId = (session["primaryAccounts"] as JsonObject)!.First().Value!.GetValue<string>();

        var body = new JsonObject
        {
            ["using"] = new JsonArray("urn:ietf:params:jmap:core", "urn:stalwart:jmap"),
            ["methodCalls"] = new JsonArray(new JsonArray($"{objectType}/get", new JsonObject { ["accountId"] = accountId }, "0")),
        };
        request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");

        var response = JsonNode.Parse(await (await client.SendAsync(request)).Content.ReadAsStringAsync())!;
        var args = (response["methodResponses"] as JsonArray)![0]![1]!;
        return (args["list"] as JsonArray)?.OfType<JsonNode>().ToList() ?? [];
    }
}
#endif
