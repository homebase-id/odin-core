using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Odin.Core.Logging.LogLevelOverwrite.Serilog;
using Serilog.Core;
using Serilog.Events;
using Serilog.Parsing;

namespace Odin.Core.Tests.Logging.LogLevelOverwrite.Serilog;

public class LogEventFilterSinkTest
{
    private sealed class CapturingSink : ILogEventSink
    {
        public List<LogEvent> Events { get; } = new();
        public void Emit(LogEvent logEvent) => Events.Add(logEvent);
    }

    private static LogEvent MakeEvent(LogEventLevel level, Exception exception, string message = "some message")
    {
        var template = new MessageTemplateParser().Parse(message);
        return new LogEvent(
            DateTimeOffset.UnixEpoch,
            level,
            exception,
            template,
            Enumerable.Empty<LogEventProperty>());
    }

    private static LogEvent EmitAndCapture(LogEvent input)
    {
        var capturing = new CapturingSink();
        var sink = new LogEventFilterSink(capturing);
        sink.Emit(input);
        Assert.That(capturing.Events.Count, Is.EqualTo(1), "Expected exactly one event to pass through");
        return capturing.Events.Single();
    }

    [Test]
    public void ClientDisconnectDuringTlsWrite_IsDemotedToVerbose()
    {
        // Mirrors the real exception: IOException whose inner message carries the OpenSSL token.
        var exception = new IOException(
            "The encryption operation failed, see inner exception.",
            new Exception("Encrypt failed with OpenSSL error - SSL_ERROR_SYSCALL."));

        var result = EmitAndCapture(MakeEvent(LogEventLevel.Error, exception));

        Assert.That(result.Level, Is.EqualTo(LogEventLevel.Verbose));
        Assert.That(result.Exception, Is.SameAs(exception), "Exception must be preserved");
    }

    [Test]
    public void CertPrivateKeyMisconfiguration_IsDemotedToWarning()
    {
        var exception = new NotSupportedException(
            "The server mode SSL must use a certificate with the associated private key.");

        var result = EmitAndCapture(MakeEvent(LogEventLevel.Error, exception));

        Assert.That(result.Level, Is.EqualTo(LogEventLevel.Warning));
    }

    [Test]
    public void UnrelatedError_PassesThroughUnchanged()
    {
        var exception = new InvalidOperationException("something genuinely wrong");

        var result = EmitAndCapture(MakeEvent(LogEventLevel.Error, exception));

        Assert.That(result.Level, Is.EqualTo(LogEventLevel.Error));
    }

    [Test]
    public void NonIoExceptionContainingSyscallToken_IsNotDemoted()
    {
        // Only IOException-wrapped SSL_ERROR_SYSCALL is the client-disconnect signature.
        var exception = new InvalidOperationException("unexpected SSL_ERROR_SYSCALL in some other context");

        var result = EmitAndCapture(MakeEvent(LogEventLevel.Error, exception));

        Assert.That(result.Level, Is.EqualTo(LogEventLevel.Error));
    }
}
