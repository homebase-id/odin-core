using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using Odin.Core.Logging.Redaction;
using Odin.Core.Logging.Redaction.Serilog;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Serilog.Extensions.Logging;

namespace Odin.Core.Tests.Logging.Redaction;

public class LogRedactionTests
{
    [TestCase("/api/owner/v1/config/can-use-auto-recovery?frt=f00d", "/api/owner/v1/config/can-use-auto-recovery?frt=[redacted]")]
    [TestCase("/api/registration/v1/registration/create-own-domain-zone/a.dk?invitation-code=c0de",
        "/api/registration/v1/registration/create-own-domain-zone/a.dk?invitation-code=[redacted]")]
    [TestCase("/api/owner/v1/security/recovery/verify-enter?id=n0nce&token=", "/api/owner/v1/security/recovery/verify-enter?id=[redacted]&token=")]
    [TestCase("/api/v2/links/extract?url=https%3A%2F%2Fa.com%2Fx%3Fsig%3Ds3cret", "/api/v2/links/extract?url=[redacted]")]
    [TestCase("/x?Token=s3cret&fileId=42&FILEID=43", "/x?Token=[redacted]&fileId=42&FILEID=43")]
    [TestCase("/x?somethingNew=v&width=10", "/x?somethingNew=[redacted]&width=10")]
    [TestCase("/api/registration/v1/registration/is-valid-invitation-code/c0de",
        "/api/registration/v1/registration/is-valid-invitation-code/[redacted]")]
    [TestCase("/api/registration/v1/registration/is-valid-invitation-code/c0de?x=1",
        "/api/registration/v1/registration/is-valid-invitation-code/[redacted]?x=[redacted]")]
    [TestCase("/api/v2/capi/validate/capi.sam.dotyou.cloud~5e55", "/api/v2/capi/validate/[redacted]")]
    [TestCase("https://frodo.dotyou.cloud:443/api/v2/capi/validate/capi.sam.dotyou.cloud~5e55",
        "https://frodo.dotyou.cloud:443/api/v2/capi/validate/[redacted]")]
    public void CredentialsInAUrlAreRedacted(string url, string expected)
    {
        Assert.That(LogRedaction.Url(url), Is.EqualTo(expected));
    }

    [TestCase("")]
    [TestCase("/api/v2/drives/9ff8/files")]
    [TestCase("/api/v2/drives/9ff8/files?fileId=1&key=pk1&width=10&ss=abc")]
    [TestCase("/api/registration/v1/registration/is-invitation-code-needed")]
    [TestCase("/api/registration/v1/registration/is-valid-invitation-code/")]
    [TestCase("/x?flag&y=")]
    public void AUrlWithoutCredentialsIsUnchanged(string url)
    {
        Assert.That(LogRedaction.Url(url), Is.EqualTo(url));
    }

    [TestCase("Authorization")]
    [TestCase("authorization")]
    [TestCase("Cookie")]
    [TestCase("X-DI-ClientAuthToken")]
    [TestCase("Sec-WebSocket-Protocol")]
    [TestCase("X-Something-New")]
    public void AHeaderNotOnTheAllowlistIsRedacted(string name)
    {
        Assert.That(LogRedaction.HeaderValue(name, "Bearer s3cret"), Is.EqualTo(LogRedaction.Redacted));
    }

    [TestCase("User-Agent")]
    [TestCase("content-length")]
    [TestCase("Odin-Correlation-Id")]
    public void AHeaderOnTheAllowlistKeepsItsValue(string name)
    {
        Assert.That(LogRedaction.HeaderValue(name, "v1"), Is.EqualTo("v1"));
    }

    [Test]
    public void AFingerprintTellsSecretsApartWithoutShowingThem()
    {
        var a = LogRedaction.Fingerprint("device-token-a");
        Assert.That(a, Has.Length.EqualTo(8));
        Assert.That(a, Is.EqualTo(LogRedaction.Fingerprint("device-token-a")));
        Assert.That(a, Is.Not.EqualTo(LogRedaction.Fingerprint("device-token-b")));
        Assert.That("device-token-a", Does.Not.Contain(a));
        Assert.That(LogRedaction.Fingerprint(null), Is.EqualTo("(none)"));
    }

    [Test]
    public void EveryUrlOnALogLineIsRedacted()
    {
        // The way ASP.NET Core adds RequestPath: from a logging scope, through Microsoft.Extensions.Logging
        var sink = new CollectingSink();
        using var serilog = new LoggerConfiguration().Enrich.WithUrlRedaction().WriteTo.Sink(sink).CreateLogger();
        using var factory = new SerilogLoggerFactory(serilog);
        var logger = factory.CreateLogger("test");

        using (logger.BeginScope(new Dictionary<string, object> { ["RequestPath"] = "/api/v2/capi/validate/capi.sam~5e55" }))
        {
            logger.LogInformation("request {Path} fetched {Url} unchanged {Other}",
                "/api/owner/v1/config/can-use-auto-recovery?frt=f00d",
                new Uri("https://a.com/x?sig=s3cret"),
                "/other?token=kept-because-not-a-url-property");
        }

        Assert.That(sink.Events, Has.Count.EqualTo(1));
        var properties = sink.Events[0].Properties;
        Assert.That(properties["RequestPath"].ToString(), Is.EqualTo("\"/api/v2/capi/validate/[redacted]\""));
        Assert.That(properties["Path"].ToString(), Is.EqualTo("\"/api/owner/v1/config/can-use-auto-recovery?frt=[redacted]\""));
        Assert.That(properties["Url"].ToString(), Is.EqualTo("\"https://a.com/x?sig=[redacted]\""));
        Assert.That(properties["Other"].ToString(), Is.EqualTo("\"/other?token=kept-because-not-a-url-property\""));
        Assert.That(sink.Events[0].RenderMessage(), Does.Not.Contain("f00d").And.Not.Contain("s3cret"),
            $"actual: {sink.Events[0].RenderMessage()}");
    }

    private class CollectingSink : ILogEventSink
    {
        public List<LogEvent> Events { get; } = [];
        public void Emit(LogEvent logEvent) => Events.Add(logEvent);
    }
}
