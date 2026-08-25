using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using DnsClient;
using DnsClient.Protocol;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using Odin.Core.Http;
using Odin.Services.Configuration;
using Odin.Services.Email;

namespace Odin.Services.Tests.Email;

#nullable enable

public class EmailInfraVerifierTest
{
    private readonly Mock<ILogger<EmailInfraVerifier>> _logger = new();
    private readonly Mock<ILookupClient> _dnsClient = new();
    private readonly Mock<IDynamicHttpClientFactory> _httpClientFactory = new();
    private readonly Mock<IEmailSender> _emailSender = new();

    [SetUp]
    public void SetUp()
    {
        _logger.Reset();
        _dnsClient.Reset();
        _httpClientFactory.Reset();
        _emailSender.Reset();
        _emailSender
            .Setup(x => x.VerifyCredentialsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
    }

    private EmailInfraVerifier CreateVerifier(OdinConfiguration.EmailSection email)
    {
        var configuration = new OdinConfiguration { Email = email };
        return new EmailInfraVerifier(
            _logger.Object, configuration, _dnsClient.Object, _httpClientFactory.Object, _emailSender.Object);
    }

    private void VerifyLogged(LogLevel level, string substring, Times times)
    {
        _logger.Verify(x => x.Log(
            level,
            It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((o, _) => o.ToString()!.Contains(substring)),
            It.IsAny<Exception?>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()), times);
    }

    //
    // Configuration findings (non-retryable, logged once)
    //

    [Test]
    public void ProviderNoneHasNothingToVerify()
    {
        var verifier = CreateVerifier(new OdinConfiguration.EmailSection());

        Assert.That(verifier.LogConfigurationFindings(), Is.False);
        VerifyLogged(LogLevel.Information, "system mail is disabled", Times.Once());
        VerifyLogged(LogLevel.Error, "", Times.Never());
    }

    [Test]
    public void TenantMailWithoutProviderIsAConfigurationError()
    {
        var verifier = CreateVerifier(new OdinConfiguration.EmailSection
        {
            TenantMail = new OdinConfiguration.TenantMailSection { Enabled = true },
        });

        Assert.That(verifier.LogConfigurationFindings(), Is.False);
        VerifyLogged(LogLevel.Error, "Email:Provider is 'None'", Times.Once());
    }

    [Test]
    public void DeprecatedMailgunSectionLogsMoveItWarning()
    {
        var verifier = CreateVerifier(new OdinConfiguration.EmailSection
        {
            Provider = EmailProvider.Mailgun,
            UsingDeprecatedMailgunSection = true,
        });

        Assert.That(verifier.LogConfigurationFindings(), Is.True);
        VerifyLogged(LogLevel.Warning, "deprecated top-level Mailgun section", Times.Once());
    }

    [Test]
    public void SmtpProviderReportsItsRelayAndVerifiesTenantMailInfra()
    {
        var verifier = CreateVerifier(new OdinConfiguration.EmailSection
        {
            Provider = EmailProvider.Smtp,
            Smtp = new OdinConfiguration.SmtpProviderSection { RelayHost = "localhost", RelayPort = 2525 },
            TenantMail = new OdinConfiguration.TenantMailSection { Enabled = true },
        });

        Assert.That(verifier.LogConfigurationFindings(), Is.True);

        // Was a "not implemented yet" warning until SmtpSender landed; now it says where mail
        // goes, which is the thing an operator actually needs from this line.
        VerifyLogged(LogLevel.Information, "submitted to localhost:2525", Times.Once());
    }

    //
    // Network checks (retried by the background service)
    //

    private static OdinConfiguration.EmailSection TenantMailSection(
        EmailProvider provider = EmailProvider.SendGrid, string canaryDomain = "")
    {
        return new OdinConfiguration.EmailSection
        {
            Provider = provider,
            TenantMail = new OdinConfiguration.TenantMailSection
            {
                Enabled = true,
                CanaryDomain = canaryDomain,
                MxNodes = ["node-a.example.com", "node-b.example.com"],
                SpfIncludeTarget = "_spf.example.com",
                DmarcReportEmail = "dmarc-reports@example.com",
                TlsReportEmail = "tls-reports@example.com",
            }
        };
    }

    private void SetupARecords(string node, params string[] ips)
    {
        _dnsClient
            .Setup(x => x.QueryAsync(node, QueryType.A, It.IsAny<QueryClass>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response(ips.Select(ip => (DnsResourceRecord)A(node, ip)).ToArray()));

        // Reverse DNS resolves and forward-confirms by default, so tests about SPF, MTA-STS
        // and the like are not all rewritten as reverse-DNS tests. The rDNS cases below
        // override this per address.
        foreach (var ip in ips)
        {
            SetupReverseDns(ip, node);
            SetupForwardConfirm(node, ip);
        }
    }

    /// <summary>PTR for <paramref name="ip"/>. Pass no names for "this IP has no PTR".</summary>
    private void SetupReverseDns(string ip, params string[] ptrNames)
    {
        _dnsClient
            .Setup(x => x.QueryReverseAsync(IPAddress.Parse(ip), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response(ptrNames
                .Select(name => (DnsResourceRecord)Ptr(ip, name))
                .ToArray()));
    }

    /// <summary>The forward half of forward-confirmed reverse DNS: PTR name -> address.</summary>
    private void SetupForwardConfirm(string ptrName, params string[] ips)
    {
        _dnsClient
            .Setup(x => x.QueryAsync(ptrName, QueryType.A, It.IsAny<QueryClass>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response(ips.Select(ip => (DnsResourceRecord)A(ptrName, ip)).ToArray()));
    }

    private static PtrRecord Ptr(string ip, string ptrName) =>
        new(new ResourceRecordInfo(
                $"{string.Join('.', ip.Split('.').Reverse())}.in-addr.arpa.",
                ResourceRecordType.PTR, QueryClass.IN, 60, 0),
            DnsString.Parse(ptrName));

    private void SetupTxtRecords(string owner, params string[][] txtChunks)
    {
        _dnsClient
            .Setup(x => x.QueryAsync(owner, QueryType.TXT, It.IsAny<QueryClass>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response(txtChunks.Select(chunks => (DnsResourceRecord)Txt(owner, chunks)).ToArray()));
    }

    /// <summary>
    /// Receiving servers test forward-confirmed reverse DNS, so we do too. An MX address
    /// with no PTR gets its mail spam-foldered or refused, and every forward record can be
    /// perfect while this fails: the reverse zone belongs to the hosting provider, not us.
    /// </summary>
    /// <summary>
    /// Forward-confirmation is necessary but not sufficient. A hosting provider's default PTR
    /// confirms perfectly while matching nothing we send, and receivers that compare reverse
    /// DNS against the EHLO name penalise that - so it is reported, but as a warning, because
    /// mail still flows and the fix belongs to the hosting provider.
    /// </summary>
    [Test]
    public async Task ItShouldNotErrorWhenThePtrConfirmsButDoesNotMatchTheHostname()
    {
        SetupARecords("node-a.example.com", "10.0.0.1");
        SetupARecords("node-b.example.com", "10.0.0.2");
        SetupTxtRecords("_spf.example.com", ["v=spf1 include:sendgrid.net -all"]);

        // The Hetzner-style case: a generic PTR that forward-confirms to the same address
        SetupReverseDns("10.0.0.1", "static.1.0.0.10.clients.example-host.de");
        SetupForwardConfirm("static.1.0.0.10.clients.example-host.de", "10.0.0.1");

        var errors = await CreateVerifier(TenantMailSection()).VerifyNetworkAsync();

        Assert.That(errors, Is.Empty, "a confirming PTR must not fail the host's boot check");
    }

    [Test]
    public async Task ItShouldErrorWhenAnMxAddressHasNoReverseDns()
    {
        SetupARecords("node-a.example.com", "10.0.0.1");
        SetupARecords("node-b.example.com", "10.0.0.2");
        SetupTxtRecords("_spf.example.com", ["v=spf1 include:sendgrid.net -all"]);
        SetupReverseDns("10.0.0.1"); // no PTR at all

        var errors = await CreateVerifier(TenantMailSection()).VerifyNetworkAsync();

        Assert.That(errors, Has.Exactly(1).Contains("has no PTR record"));
    }

    [Test]
    public async Task ItShouldErrorWhenTheReverseRecordDoesNotConfirmForward()
    {
        SetupARecords("node-a.example.com", "10.0.0.1");
        SetupARecords("node-b.example.com", "10.0.0.2");
        SetupTxtRecords("_spf.example.com", ["v=spf1 include:sendgrid.net -all"]);

        // PTR points at a name that resolves somewhere else - the classic stale-rDNS case
        SetupReverseDns("10.0.0.1", "old-host.example.com");
        SetupForwardConfirm("old-host.example.com", "10.9.9.9");

        var errors = await CreateVerifier(TenantMailSection()).VerifyNetworkAsync();

        Assert.That(errors, Has.Exactly(1).Contains("does not resolve back"));
    }

    [Test]
    public async Task ItShouldPassWhenEverythingResolves()
    {
        SetupARecords("node-a.example.com", "10.0.0.1");
        SetupARecords("node-b.example.com", "10.0.0.2");
        SetupTxtRecords("_spf.example.com", ["v=spf1 include:sendgrid.net -all"]);

        var errors = await CreateVerifier(TenantMailSection()).VerifyNetworkAsync();

        Assert.That(errors, Is.Empty);
    }

    [Test]
    public async Task ItShouldReportFailedProviderCredentials()
    {
        _emailSender
            .Setup(x => x.VerifyCredentialsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var errors = await CreateVerifier(new OdinConfiguration.EmailSection
        {
            Provider = EmailProvider.SendGrid,
        }).VerifyNetworkAsync();

        Assert.That(errors.Single(), Does.Contain("credential check failed"));
    }

    [Test]
    public async Task ItShouldReportAThrowingCredentialCheckAsAFailureNotACrash()
    {
        _emailSender
            .Setup(x => x.VerifyCredentialsAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("connection refused"));

        var errors = await CreateVerifier(new OdinConfiguration.EmailSection
        {
            Provider = EmailProvider.SendGrid,
        }).VerifyNetworkAsync();

        Assert.That(errors.Single(), Does.Contain("connection refused"));
    }

    [Test]
    public async Task ItShouldReportEveryMxNodeWithoutAnARecord()
    {
        // HA only works if ALL listed nodes serve - each missing node is its own error
        SetupARecords("node-a.example.com", "10.0.0.1");
        SetupARecords("node-b.example.com" /* no ips */);
        SetupTxtRecords("_spf.example.com", ["v=spf1 include:sendgrid.net -all"]);

        var errors = await CreateVerifier(TenantMailSection()).VerifyNetworkAsync();

        Assert.That(errors.Single(), Does.Contain("node-b.example.com"));
    }

    [Test]
    public async Task ItShouldReportACnameMxNodeEvenThoughItResolves()
    {
        // Recursive A resolution follows the CNAME and returns addresses, so without
        // inspecting the chain an RFC 2181-violating CNAME node would silently pass
        _dnsClient
            .Setup(x => x.QueryAsync("node-a.example.com", QueryType.A, It.IsAny<QueryClass>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response([
                Cname("node-a.example.com.", "real-host.example.com."),
                A("real-host.example.com.", "10.0.0.1"),
            ]));
        SetupARecords("node-b.example.com", "10.0.0.2");
        SetupTxtRecords("_spf.example.com", ["v=spf1 include:sendgrid.net -all"]);

        var errors = await CreateVerifier(TenantMailSection()).VerifyNetworkAsync();

        Assert.That(errors.Single(), Does.Contain("node-a.example.com").And.Contain("CNAME"));
    }

    [Test]
    public async Task ItShouldReportAMissingSpfIncludeTarget()
    {
        SetupARecords("node-a.example.com", "10.0.0.1");
        SetupARecords("node-b.example.com", "10.0.0.2");
        SetupTxtRecords("_spf.example.com", ["some-unrelated-verification-token"]);

        var errors = await CreateVerifier(TenantMailSection()).VerifyNetworkAsync();

        Assert.That(errors.Single(), Does.Contain("_spf.example.com"));
    }

    [Test]
    public async Task ItShouldReassembleChunkedSpfTxtRecordsBeforeMatching()
    {
        SetupARecords("node-a.example.com", "10.0.0.1");
        SetupARecords("node-b.example.com", "10.0.0.2");
        // A >255-byte TXT value arrives as multiple character strings of one record
        SetupTxtRecords("_spf.example.com", ["v=spf1 include:", "sendgrid.net -all"]);

        var errors = await CreateVerifier(TenantMailSection()).VerifyNetworkAsync();

        Assert.That(errors, Is.Empty);
    }

    [Test]
    public async Task ItShouldNotTouchDnsWhenTenantMailIsDisabled()
    {
        var errors = await CreateVerifier(new OdinConfiguration.EmailSection
        {
            Provider = EmailProvider.SendGrid,
        }).VerifyNetworkAsync();

        Assert.That(errors, Is.Empty);
        _dnsClient.VerifyNoOtherCalls();
    }

    //
    // MTA-STS policy endpoint (canary domain)
    //

    private void SetupMtaStsEndpoint(HttpStatusCode statusCode, string body)
    {
        var handler = new StubHttpMessageHandler(statusCode, body);
        _httpClientFactory
            .Setup(x => x.CreateClient(It.IsAny<string>(), It.IsAny<Action<ClientHandlerConfig>?>()))
            .Returns(new HttpClient(handler));
    }

    [Test]
    public async Task ItShouldAcceptAValidMtaStsPolicy()
    {
        SetupARecords("node-a.example.com", "10.0.0.1");
        SetupARecords("node-b.example.com", "10.0.0.2");
        SetupTxtRecords("_spf.example.com", ["v=spf1 include:sendgrid.net -all"]);
        SetupMtaStsEndpoint(HttpStatusCode.OK, "version: STSv1\nmode: testing\nmx: node-a.example.com\nmax_age: 86400\n");

        var errors = await CreateVerifier(TenantMailSection(canaryDomain: "canary.example.com")).VerifyNetworkAsync();

        Assert.That(errors, Is.Empty);
    }

    [Test]
    public async Task ItShouldReportAnUnreachableOrInvalidMtaStsPolicy()
    {
        SetupARecords("node-a.example.com", "10.0.0.1");
        SetupARecords("node-b.example.com", "10.0.0.2");
        SetupTxtRecords("_spf.example.com", ["v=spf1 include:sendgrid.net -all"]);

        SetupMtaStsEndpoint(HttpStatusCode.NotFound, "");
        var notFound = await CreateVerifier(TenantMailSection(canaryDomain: "canary.example.com")).VerifyNetworkAsync();
        Assert.That(notFound.Single(), Does.Contain("mta-sts.canary.example.com"));

        SetupMtaStsEndpoint(HttpStatusCode.OK, "<html>not a policy</html>");
        var wrongBody = await CreateVerifier(TenantMailSection(canaryDomain: "canary.example.com")).VerifyNetworkAsync();
        Assert.That(wrongBody.Single(), Does.Contain("not serving a valid policy"));
    }

    //
    // Record construction helpers (pattern from DnsHealthServiceTest)
    //

    private static IDnsQueryResponse Response(DnsResourceRecord[] answers)
    {
        var mock = new Mock<IDnsQueryResponse>();
        mock.SetupGet(x => x.HasError).Returns(false);
        mock.SetupGet(x => x.Answers).Returns(answers);
        mock.SetupGet(x => x.Authorities).Returns([]);
        return mock.Object;
    }

    private static ARecord A(string owner, string ip)
    {
        var info = new ResourceRecordInfo(DnsString.Parse(owner), ResourceRecordType.A, QueryClass.IN, 3600, 0);
        return new ARecord(info, IPAddress.Parse(ip));
    }

    private static CNameRecord Cname(string owner, string target)
    {
        var info = new ResourceRecordInfo(DnsString.Parse(owner), ResourceRecordType.CNAME, QueryClass.IN, 3600, 0);
        return new CNameRecord(info, DnsString.Parse(target));
    }

    private static TxtRecord Txt(string owner, string[] chunks)
    {
        var info = new ResourceRecordInfo(DnsString.Parse(owner), ResourceRecordType.TXT, QueryClass.IN, 3600, 0);
        return new TxtRecord(info, chunks, chunks);
    }

    private class StubHttpMessageHandler(HttpStatusCode statusCode, string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(body),
            });
        }
    }
}
