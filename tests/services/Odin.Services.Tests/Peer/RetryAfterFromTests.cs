using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using Odin.Core.Time;
using Odin.Services.Base;
using Odin.Services.Configuration;
using Odin.Services.Peer.Outgoing.Drive.Transfer.Outbox;
using Refit;

namespace Odin.Services.Tests.Peer;

/// <summary>
/// Covers <c>OutboxWorkerBase.RetryAfterFrom</c>: only a paused (503) or out-of-quota (507) recipient
/// that says when to come back gets its item deferred instead of counting an attempt.
/// </summary>
public class RetryAfterFromTests
{
    private sealed class TestWorker(OutboxFileItem fileItem, OdinConfiguration configuration)
        : OutboxWorkerBase(fileItem, NullLogger.Instance, null, configuration)
    {
        public TimeSpan? Read<T>(ApiResponse<T> response) => RetryAfterFrom(response);

        protected override Task<UnixTimeUtc> HandleRecoverableTransferStatus(IOdinContext odinContext,
            OdinOutboxProcessingException e) => Task.FromResult(UnixTimeUtc.Now());

        protected override Task HandleUnrecoverableTransferStatus(OdinOutboxProcessingException e,
            IOdinContext odinContext) => Task.CompletedTask;
    }

    private static ApiResponse<string> Response(HttpStatusCode statusCode, RetryConditionHeaderValue? retryAfter = null)
    {
        var message = new HttpResponseMessage(statusCode);
        if (retryAfter != null)
        {
            message.Headers.RetryAfter = retryAfter;
        }

        return new ApiResponse<string>(message, null, new RefitSettings());
    }

    private static TestWorker Worker()
    {
        return new TestWorker(new OutboxFileItem(), new OdinConfiguration());
    }

    [TestCase(HttpStatusCode.ServiceUnavailable)]
    [TestCase(HttpStatusCode.InsufficientStorage)]
    public void ReadsTheDeltaForm(HttpStatusCode statusCode)
    {
        var result = Worker().Read(Response(statusCode, new RetryConditionHeaderValue(TimeSpan.FromSeconds(600))));
        Assert.That(result, Is.EqualTo(TimeSpan.FromSeconds(600)));
    }

    [Test]
    public void ReadsTheHttpDateForm()
    {
        var when = DateTimeOffset.UtcNow.AddMinutes(30);
        var result = Worker().Read(Response(HttpStatusCode.InsufficientStorage, new RetryConditionHeaderValue(when)));

        Assert.That(result, Is.Not.Null);
        // The header carries whole seconds, and time passes between building and reading it
        Assert.That(result!.Value.TotalMinutes, Is.EqualTo(30).Within(1));
    }

    [Test]
    public void ADateInThePastReadsAsZeroRatherThanNegative()
    {
        var when = DateTimeOffset.UtcNow.AddMinutes(-5);
        var result = Worker().Read(Response(HttpStatusCode.ServiceUnavailable, new RetryConditionHeaderValue(when)));

        Assert.That(result, Is.EqualTo(TimeSpan.Zero));
    }

    [Test]
    public void NoHeaderMeansNoDeferral()
    {
        Assert.That(Worker().Read(Response(HttpStatusCode.ServiceUnavailable)), Is.Null);
    }

    [TestCase(HttpStatusCode.InternalServerError)]
    [TestCase(HttpStatusCode.BadGateway)]
    [TestCase(HttpStatusCode.Conflict)]
    [TestCase(HttpStatusCode.TooManyRequests)]
    public void OtherStatusesAreNotDeferredEvenWithTheHeader(HttpStatusCode statusCode)
    {
        var result = Worker().Read(Response(statusCode, new RetryConditionHeaderValue(TimeSpan.FromSeconds(600))));
        Assert.That(result, Is.Null);
    }
}
