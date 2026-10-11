#nullable enable
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using Odin.Core.Util;
using Odin.Services.Email.Relay;
using Odin.Services.JobManagement;
using Odin.Services.Registry.Registration;

namespace Odin.Services.Tests.Email.Relay;

/// <summary>
/// On 2026-10-07 the relay refused a domain and the job spent ten retries learning the same
/// answer, then left it only on its own soon-purged row. These pin the replacement: a refusal
/// stops at once and is recorded where the health check reads it.
/// </summary>
public class MailRelayOnboardingJobTest
{
    private const string Domain = "frodo.example.com";

    private readonly Mock<IMailRelayProvider> _relay = new();
    private readonly Mock<IMailRelayFailureStore> _failureStore = new();
    private readonly Mock<IIdentityRegistrationService> _registration = new();

    [SetUp]
    public void SetUp()
    {
        _relay.Reset();
        _failureStore.Reset();
        _registration.Reset();
        _relay.Setup(x => x.IsConfigured).Returns(true);
    }

    [Test]
    public async Task ItShouldStopAtOnceAndRecordTheReasonWhenTheRelayRefuses()
    {
        var refusal = new MailRelayException("Relay: POST /domain/add returned 400: plan limit", 400);
        _relay.Setup(x => x.EnsureDomainAsync(It.IsAny<AsciiDomainName>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(refusal);

        var result = await NewJob().Run(CancellationToken.None);

        Assert.That(result.Result, Is.EqualTo(RunResult.Abort), $"result was {result.Result}");
        _failureStore.Verify(x => x.RecordAsync(Domain, refusal.Message), Times.Once);
        _registration.Verify(x => x.WriteOnActivationRecords(It.IsAny<AsciiDomainName>(), It.IsAny<List<DnsConfig>>()),
            Times.Never);
    }

    [Test]
    public void ItShouldRecordTheReasonAndLeaveATransientFailureToTheRetries()
    {
        var blip = new HttpRequestException("connection reset");
        _relay.Setup(x => x.EnsureDomainAsync(It.IsAny<AsciiDomainName>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(blip);

        var thrown = Assert.ThrowsAsync<HttpRequestException>(() => NewJob().Run(CancellationToken.None));

        Assert.That(thrown, Is.SameAs(blip), $"threw {thrown?.GetType().Name}: {thrown?.Message}");
        _failureStore.Verify(x => x.RecordAsync(Domain, blip.Message), Times.Once);
    }

    [Test]
    public async Task ItShouldClearAnEarlierFailureOnceTheRelayAccepts()
    {
        _relay.Setup(x => x.EnsureDomainAsync(It.IsAny<AsciiDomainName>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MailRelayDomainState { Domain = Domain });
        _relay.Setup(x => x.VerifyDomainAsync(It.IsAny<AsciiDomainName>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MailRelayDomainState { Domain = Domain, Verified = true });
        _registration.Setup(x => x.WriteOnActivationRecords(It.IsAny<AsciiDomainName>(), It.IsAny<List<DnsConfig>>()))
            .ReturnsAsync(true);

        var result = await NewJob().Run(CancellationToken.None);

        Assert.That(result.Result, Is.EqualTo(RunResult.Success), $"result was {result.Result}");
        _failureStore.Verify(x => x.ClearAsync(Domain), Times.Once);
        _failureStore.Verify(x => x.RecordAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    /// <summary>
    /// The relay needs the same records whoever publishes them. When the DNS is not ours to
    /// write, the owner adds them by hand - and the relay must still be asked to verify, or a
    /// domain with correct records stays unverified for good.
    /// </summary>
    [TestCase(true, RunResult.Success)]
    [TestCase(false, RunResult.Defer)]
    public async Task ItShouldVerifyEvenWhenTheOwnerPublishesTheRecords(bool relayVerifies, RunResult expected)
    {
        _relay.Setup(x => x.EnsureDomainAsync(It.IsAny<AsciiDomainName>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MailRelayDomainState { Domain = Domain });
        _relay.Setup(x => x.VerifyDomainAsync(It.IsAny<AsciiDomainName>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MailRelayDomainState { Domain = Domain, Verified = relayVerifies });
        _registration.Setup(x => x.WriteOnActivationRecords(It.IsAny<AsciiDomainName>(), It.IsAny<List<DnsConfig>>()))
            .ReturnsAsync(false);

        var result = await NewJob().Run(CancellationToken.None);

        _relay.Verify(x => x.VerifyDomainAsync(It.IsAny<AsciiDomainName>(), It.IsAny<CancellationToken>()), Times.Once);
        Assert.That(result.Result, Is.EqualTo(expected), $"result was {result.Result}");
    }

    /// <summary>
    /// Once registered and published (by an earlier run, or by the repair button inline), a
    /// retry is one relay call: verification. Not another registration, failure-store write
    /// and DNS write every ten minutes for a day.
    /// </summary>
    [Test]
    public async Task ItShouldOnlyVerifyOnceSetupIsDone()
    {
        _relay.Setup(x => x.VerifyDomainAsync(It.IsAny<AsciiDomainName>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MailRelayDomainState { Domain = Domain, Verified = false });
        var job = NewJob();
        job.Data.SetupDone = true;

        var result = await job.Run(CancellationToken.None);

        Assert.That(result.Result, Is.EqualTo(RunResult.Defer), $"result was {result.Result}");
        _relay.Verify(x => x.EnsureDomainAsync(It.IsAny<AsciiDomainName>(), It.IsAny<CancellationToken>()), Times.Never);
        _registration.Verify(x => x.WriteOnActivationRecords(It.IsAny<AsciiDomainName>(), It.IsAny<List<DnsConfig>>()),
            Times.Never);
        _failureStore.Verify(x => x.ClearAsync(It.IsAny<string>()), Times.Never);
    }

    [Test]
    public void ItShouldBeUniquePerDomainWhateverTheCase()
    {
        var job = NewJob();
        var upper = NewJob();
        upper.Data.Domain = Domain.ToUpperInvariant();

        Assert.That(job.CreateJobHash(), Is.EqualTo(MailRelayOnboardingJob.JobHashFor(Domain)));
        Assert.That(upper.CreateJobHash(), Is.EqualTo(job.CreateJobHash()),
            $"{upper.CreateJobHash()} vs {job.CreateJobHash()}");
    }

    //

    private MailRelayOnboardingJob NewJob()
    {
        return new MailRelayOnboardingJob(
            new Mock<ILogger<MailRelayOnboardingJob>>().Object,
            _relay.Object,
            _failureStore.Object,
            _registration.Object)
        {
            Data = new MailRelayOnboardingJobData { Domain = Domain },
        };
    }
}
