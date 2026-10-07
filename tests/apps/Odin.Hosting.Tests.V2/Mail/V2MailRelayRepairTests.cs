using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Odin.Core.Util;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Mail;
using Odin.Services.Email.Relay;
using Odin.Services.Registry.Registration;

#nullable enable

namespace Odin.Hosting.Tests.V2.Mail;

/// <summary>
/// The owner's repair button against an outbound relay that refuses, then accepts.
///
/// On 2026-10-07 the relay refused a tenant's domain (a plan's sender cap), the button swallowed
/// that as a warning, and the owner was told nothing. These pin the replacement: the refusal comes
/// back in the response and is kept where the health check reads it; once the relay accepts, the
/// same button registers the domain, returns its records, and clears the old reason.
/// </summary>
/// <remarks>
/// The relay verdict on the health surface (NotRegistered with the reason) is covered in
/// <c>DnsHealthServiceTest</c>: it needs authoritative DNS lookups this host cannot make.
/// </remarks>
[TestFixture]
public class V2MailRelayRepairTests : V2Fixture
{
    private const string PlanLimitRefusal =
        "Relay: POST /domain/add returned 400: Free plans can add a maximum of 5 verified senders";

    private readonly FakeMailRelayProvider _relay = new();

    protected override IReadOnlyDictionary<string, string?> ConfigOverrides =>
        new Dictionary<string, string?>
        {
            ["Email:TenantMail:Enabled"] = "true",
            ["Email:TenantMail:MxNodes:0"] = "mx1.dotyou.cloud",
            ["Email:TenantMail:SpfIncludeTarget"] = "_spf.dotyou.cloud",
            ["Email:TenantMail:DmarcReportEmail"] = "dmarc-reports@dotyou.cloud",
            ["Email:TenantMail:TlsReportEmail"] = "tls-reports@dotyou.cloud",
            ["Email:DkimStorageKey"] = "BAADF00DBAADF00DBAADF00DBAADF00DBAADF00DBAADF00DBAADF00DBAADF00D",
        };

    protected override void ConfigureRootContainer(ContainerBuilder cb)
    {
        cb.RegisterInstance(_relay).As<IMailRelayProvider>().SingleInstance();
    }

    // The refusal is the behaviour under test, and it is logged at Error on purpose
    protected override IReadOnlyCollection<string> ToleratedErrorLogSubstrings => ["Relay: could not register"];

    [Test]
    public async Task ItShouldReportARelayRefusalAndRepairOnceTheRelayAccepts()
    {
        var owner = await LoginAsOwner();
        var client = owner.RefitFor<IMailTestHttpClientForOwner>();
        var failureStore = Host.Server.Services.GetRequiredService<IMailRelayFailureStore>();

        // 1. The relay refuses: the button still publishes the rest, and says why the relay would not
        _relay.Refusal = PlanLimitRefusal;
        var refused = await client.PublishDnsRecords();

        Assert.That(refused.StatusCode, Is.EqualTo(HttpStatusCode.OK), $"status was {refused.StatusCode}");
        Assert.That(refused.Content!.RelayError, Is.EqualTo(PlanLimitRefusal),
            $"relayError was '{refused.Content.RelayError}'");
        Assert.That(refused.Content.Records, Is.Not.Empty, "the config-derived records are published regardless");
        Assert.That(refused.Content.Records.Any(x => x.Name == FakeMailRelayProvider.ReturnPathName), Is.False,
            "no relay records for a domain the relay refused");

        var stored = await failureStore.GetAsync(PrimaryIdentity);
        Assert.That(stored, Is.EqualTo(PlanLimitRefusal), $"stored failure was '{stored}'");

        // 2. The relay accepts (say, after a plan upgrade): the same button repairs it
        _relay.Refusal = null;
        var repaired = await client.PublishDnsRecords();

        Assert.That(repaired.StatusCode, Is.EqualTo(HttpStatusCode.OK), $"status was {repaired.StatusCode}");
        Assert.That(repaired.Content!.RelayError, Is.Null, $"relayError was '{repaired.Content.RelayError}'");
        Assert.That(repaired.Content.Records.Select(x => x.Name), Has.Member(FakeMailRelayProvider.ReturnPathName),
            $"records were: [{string.Join(", ", repaired.Content.Records.Select(x => x.Name))}]");
        Assert.That(_relay.Registered, Is.True);

        var cleared = await failureStore.GetAsync(PrimaryIdentity);
        Assert.That(cleared, Is.Null, $"stored failure was still '{cleared}'");
    }

    /// <summary>A relay that refuses with the given words while <see cref="Refusal"/> is set.</summary>
    private sealed class FakeMailRelayProvider : IMailRelayProvider
    {
        public const string ReturnPathName = "em934313";

        public string? Refusal { get; set; }
        public bool Registered { get; private set; }

        public bool IsConfigured => true;

        public Task<MailRelayDomainState> EnsureDomainAsync(AsciiDomainName domain, CancellationToken cancellationToken = default)
        {
            if (Refusal != null)
            {
                throw new MailRelayException(Refusal, 400);
            }

            Registered = true;
            return Task.FromResult(State(domain));
        }

        public Task<MailRelayDomainState?> GetDomainAsync(AsciiDomainName domain, CancellationToken cancellationToken = default)
            => Task.FromResult(Registered ? State(domain) : null);

        public Task<MailRelayDomainState> VerifyDomainAsync(AsciiDomainName domain, CancellationToken cancellationToken = default)
            => Task.FromResult(State(domain));

        public Task RemoveDomainAsync(AsciiDomainName domain, CancellationToken cancellationToken = default)
        {
            Registered = false;
            return Task.CompletedTask;
        }

        private static MailRelayDomainState State(AsciiDomainName domain) => new()
        {
            Domain = domain.DomainName,
            Records =
            [
                new DnsConfig
                {
                    Type = "CNAME", Name = ReturnPathName, Domain = $"{ReturnPathName}.{domain.DomainName}",
                    Value = "return.smtp2go.net", AltValue = "return.smtp2go.net",
                    Description = "Relay Return-Path CNAME (SPF)", Optional = true,
                },
            ],
        };
    }
}
