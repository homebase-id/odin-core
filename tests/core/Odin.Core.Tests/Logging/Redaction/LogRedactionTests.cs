using System.Collections.Generic;
using System.Linq;
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
    [TestCase("/api/owner/v1/security/recovery/verify-enter?id=1&token=s3cret", "/api/owner/v1/security/recovery/verify-enter?id=1&token=[redacted]")]
    [TestCase("/x?Token=s3cret&fileId=42", "/x?Token=[redacted]&fileId=42")]
    [TestCase("/x?a=1&client_secret=s3cret&b=2", "/x?a=1&client_secret=[redacted]&b=2")]
    [TestCase("/api/registration/v1/registration/is-valid-invitation-code/c0de",
        "/api/registration/v1/registration/is-valid-invitation-code/[redacted]")]
    [TestCase("/api/registration/v1/registration/is-valid-invitation-code/c0de?x=1",
        "/api/registration/v1/registration/is-valid-invitation-code/[redacted]?x=1")]
    public void CredentialsInAUrlAreRedacted(string url, string expected)
    {
        Assert.That(LogRedaction.Url(url), Is.EqualTo(expected));
    }

    [TestCase("")]
    [TestCase("/api/v2/drives/9ff8/files?fileId=1&key=pk1&width=10")]
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
    public void TheRequestPathOnEveryLogLineIsRedacted()
    {
        // The way ASP.NET Core does it: RequestPath comes from a logging scope, through Microsoft.Extensions.Logging
        var sink = new CollectingSink();
        using var serilog = new LoggerConfiguration().Enrich.WithRequestPathRedaction().WriteTo.Sink(sink).CreateLogger();
        using var factory = new SerilogLoggerFactory(serilog);
        var logger = factory.CreateLogger("test");

        using (logger.BeginScope(new Dictionary<string, object>
               {
                   ["RequestPath"] = "/api/registration/v1/registration/is-valid-invitation-code/c0de"
               }))
        {
            logger.LogInformation("checked");
        }

        var paths = sink.Events.Select(e => e.Properties["RequestPath"].ToString()).ToList();
        Assert.That(paths, Is.EqualTo(new[] { "\"/api/registration/v1/registration/is-valid-invitation-code/[redacted]\"" }),
            $"actual: {string.Join(", ", paths)}");
    }

    private class CollectingSink : ILogEventSink
    {
        public List<LogEvent> Events { get; } = [];
        public void Emit(LogEvent logEvent) => Events.Add(logEvent);
    }
}
