using System.Linq;
using NUnit.Framework;
using Odin.Core.Serialization;
using Odin.Services.Email.Relay;

namespace Odin.Services.Tests.Email.Relay;

/// <summary>
/// Pins SMTP2GO's wire shape against a response captured from the LIVE API, not from their
/// documentation — whose example showed tracker fields the real API populates differently.
///
/// The mapping is by explicit [JsonPropertyName], so a rename on their side lands as an empty
/// selector here. An empty selector would produce a DNS record named "._domainkey", which
/// would be published without complaint and silently never verify. This test is the thing
/// standing between that and production.
/// </summary>
public class Smtp2GoModelsTest
{
    // Verbatim from POST /domain/add for relaytest.demo.rocks, 2026-08-26.
    private const string AddResponse = """
    {
      "request_id": "0a9194b5-fccc-462b-a1bc-047cf3bc28cf",
      "data": {
        "domains": [
          {
            "domain": {
              "fulldomain": "relaytest.demo.rocks",
              "subdomain": "relaytest",
              "domain": "demo",
              "suffix": "rocks",
              "dkim_expected": "dkim.smtp2go.net",
              "dkim_selector": "s934313",
              "dkim_verified": false,
              "dkim_status": "",
              "dkim_value": "dkim.smtp2go.net",
              "rpath_expected": "return.smtp2go.net",
              "rpath_selector": "em934313",
              "rpath_verified": false,
              "rpath_status": "",
              "rpath_value": "return.smtp2go.net",
              "registrar": "",
              "setup_link": "https://app.goentri.com/share/x"
            },
            "trackers": [
              {
                "fulldomain": "link.relaytest.demo.rocks",
                "subdomain": "link.relaytest",
                "cname_expected": "track.smtp2go.net",
                "cname_verified": false,
                "cname_status": "",
                "cname_value": "",
                "enabled": false,
                "ssl_status": ""
              }
            ],
            "subaccount_access": { "subaccounts": [], "future_subaccounts": false }
          }
        ]
      }
    }
    """;

    // Verbatim from POST /domain/verify with no DNS published.
    private const string VerifyFailureResponse = """
    {
      "request_id": "7ee12310-7eeb-432b-9335-0f4aba52f4e4",
      "data": {
        "domains": [
          {
            "domain": {
              "fulldomain": "relaytest.demo.rocks",
              "dkim_selector": "s934313",
              "dkim_verified": false,
              "dkim_status": "Lookup CNAME(s934313._domainkey.relaytest.demo.rocks.) failed, 49.13.73.167 responded with a non success RCode: NXDOMAIN",
              "dkim_value": "dkim.smtp2go.net",
              "rpath_selector": "em934313",
              "rpath_verified": false,
              "rpath_status": "Lookup CNAME(em934313.relaytest.demo.rocks.) failed, 5.78.103.24 responded with a non success RCode: NXDOMAIN",
              "rpath_value": "return.smtp2go.net"
            },
            "trackers": []
          }
        ]
      }
    }
    """;

    // Verbatim from a duplicate POST /domain/add (HTTP 400).
    private const string AlreadyExistsResponse = """
    {
      "request_id": "917d393f-0e06-4a74-b71f-9699437833e4",
      "data": {
        "error": "A sender domain matching the passed value of relaytest.demo.rocks already exists - Code(IAPI1XKBMwy1GKaTsNFsDmWbgC4Rqfq)",
        "error_code": "E_ApiResponseCodes.API_EXCEPTION"
      }
    }
    """;

    [Test]
    public void ItShouldParseTheSelectorsThatBecomeDnsRecords()
    {
        var parsed = OdinSystemSerializer.Deserialize<Smtp2GoDomainResponse>(AddResponse);

        var domain = parsed!.Data.Domains.Single().Domain;
        Assert.That(domain.FullDomain, Is.EqualTo("relaytest.demo.rocks"));

        // These two values ARE the DNS records. Empty here means a record named "._domainkey".
        Assert.That(domain.DkimSelector, Is.EqualTo("s934313"));
        Assert.That(domain.DkimValue, Is.EqualTo("dkim.smtp2go.net"));
        Assert.That(domain.RpathSelector, Is.EqualTo("em934313"));
        Assert.That(domain.RpathValue, Is.EqualTo("return.smtp2go.net"));
    }

    [Test]
    public void ItShouldNotMistakeTheirSelectorForOurs()
    {
        var parsed = OdinSystemSerializer.Deserialize<Smtp2GoDomainResponse>(AddResponse);
        var selector = parsed!.Data.Domains.Single().Domain.DkimSelector;

        // The tenant's own DKIM lives at s1/s2 in the same namespace. A collision would have us
        // publish a CNAME over the tenant's signing key and silently break DKIM on everything
        // Stalwart sends. Six digits makes it impossible in practice - which is exactly why a
        // change would go unnoticed without this.
        Assert.That(selector, Is.Not.EqualTo("s1"));
        Assert.That(selector, Is.Not.EqualTo("s2"));
        Assert.That(selector, Does.StartWith("s"));
        Assert.That(selector.Length, Is.GreaterThan(2), "a bare s1/s2-shaped selector would collide");
    }

    [Test]
    public void ItShouldTreatADisabledTrackerAsNothingToPublish()
    {
        var parsed = OdinSystemSerializer.Deserialize<Smtp2GoDomainResponse>(AddResponse);
        var tracker = parsed!.Data.Domains.Single().Trackers.Single();

        // A tracker is always returned, enabled or not, and /domain/verify probes its hostname
        // regardless. Reading "not enabled + not verified" as a failure would report every
        // correctly-onboarded tenant as broken.
        Assert.That(tracker.Enabled, Is.False);
        Assert.That(tracker.CnameVerified, Is.False);
        Assert.That(tracker.CnameValue, Is.Empty);
    }

    [Test]
    public void ItShouldKeepTheResolverDiagnosticsFromAFailedVerify()
    {
        var parsed = OdinSystemSerializer.Deserialize<Smtp2GoDomainResponse>(VerifyFailureResponse);
        var domain = parsed!.Data.Domains.Single().Domain;

        Assert.That(domain.DkimVerified, Is.False);
        Assert.That(domain.RpathVerified, Is.False);

        // Worth surfacing verbatim: it names the exact lookup, which is what an owner needs.
        Assert.That(domain.DkimStatus, Does.Contain("s934313._domainkey.relaytest.demo.rocks"));
        Assert.That(domain.RpathStatus, Does.Contain("em934313.relaytest.demo.rocks"));
    }

    [Test]
    public void ItShouldNotBeAbleToTellAlreadyExistsFromAnyOtherFailure()
    {
        var parsed = OdinSystemSerializer.Deserialize<Smtp2GoDomainResponse>(AlreadyExistsResponse);

        Assert.That(parsed!.Data.Domains, Is.Empty);
        Assert.That(parsed.Data.Error, Does.Contain("already exists"));

        // The whole reason onboarding reads before it writes. This code is shared with every
        // other failure, so "add and swallow the duplicate error" would mean matching English
        // prose. If they ever make it specific, this assertion fails and we can simplify.
        Assert.That(parsed.Data.ErrorCode, Is.EqualTo("E_ApiResponseCodes.API_EXCEPTION"));
    }
}
