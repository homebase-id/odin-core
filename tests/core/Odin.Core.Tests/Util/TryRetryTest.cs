using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using Odin.Core.Util;

namespace Odin.Core.Tests.Util;

public class TryRetryTests
{
    private Mock<ILogger> _loggerMock;

    [SetUp]
    public void Setup()
    {
        _loggerMock = new Mock<ILogger>();
    }

    // Configuration Validation Tests
    [Test]
    public void WithAttempts_ValidInput_SetsAttemptsSuccessfully()
    {
        var builder = TryRetry.Create().WithAttempts(5);
        Assert.That(builder, Is.Not.Null); // Simple check; actual value is private
    }

    [Test]
    public void WithAttempts_ZeroInput_ThrowsArgumentException()
    {
        var ex = Assert.Throws<ArgumentException>(() => TryRetry.Create().WithAttempts(0));
        Assert.That(ex!.Message, Contains.Substring("Attempts must be greater than 0"));
    }

    [Test]
    public void WithAttempts_NegativeInput_ThrowsArgumentException()
    {
        var ex = Assert.Throws<ArgumentException>(() => TryRetry.Create().WithAttempts(-1));
        Assert.That(ex!.Message, Contains.Substring("Attempts must be greater than 0"));
    }

    [Test]
    public void WithDelay_ValidInput_SetsDelaySuccessfully()
    {
        var builder = TryRetry.Create().WithDelay(TimeSpan.FromSeconds(1));
        Assert.That(builder, Is.Not.Null);
    }

    [Test]
    public void WithDelay_NegativeInput_ThrowsArgumentException()
    {
        var ex = Assert.Throws<ArgumentException>(() => TryRetry.Create().WithDelay(TimeSpan.FromSeconds(-1)));
        Assert.That(ex!.Message, Contains.Substring("Delay cannot be negative"));
    }

    [Test]
    public void WithRandomDelay_ValidInput_SetsRandomDelaySuccessfully()
    {
        var builder = TryRetry.Create().WithRandomDelay(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2));
        Assert.That(builder, Is.Not.Null);
    }

    [Test]
    public void WithRandomDelay_NegativeMinInput_ThrowsArgumentException()
    {
        var ex = Assert.Throws<ArgumentException>(() => TryRetry.Create().WithRandomDelay(TimeSpan.FromSeconds(-1), TimeSpan.FromSeconds(2)));
        Assert.That(ex!.Message, Contains.Substring("Delay cannot be negative"));
    }

    [Test]
    public void WithRandomDelay_MinGreaterThanMax_ThrowsArgumentException()
    {
        var ex = Assert.Throws<ArgumentException>(() => TryRetry.Create().WithRandomDelay(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(1)));
        Assert.That(ex!.Message, Contains.Substring("Minimum delay must be less than or equal to maximum delay"));
    }

    [Test]
    public void WithExponentialBackoff_ValidInputWithoutMax_SetsBackoffSuccessfully()
    {
        var builder = TryRetry.Create().WithExponentialBackoff(TimeSpan.FromSeconds(1));
        Assert.That(builder, Is.Not.Null);
    }

    [Test]
    public void WithExponentialBackoff_ValidInputWithMax_SetsBackoffSuccessfully()
    {
        var builder = TryRetry.Create().WithExponentialBackoff(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(10));
        Assert.That(builder, Is.Not.Null);
    }

    [Test]
    public void WithExponentialBackoff_NegativeInitialDelay_ThrowsArgumentException()
    {
        var ex = Assert.Throws<ArgumentException>(() => TryRetry.Create().WithExponentialBackoff(TimeSpan.FromSeconds(-1)));
        Assert.That(ex!.Message, Contains.Substring("Initial delay must be greater than or equal to 0"));
    }

    [Test]
    public void WithExponentialBackoff_NegativeMaxBackoff_ThrowsArgumentException()
    {
        var ex = Assert.Throws<ArgumentException>(() => TryRetry.Create().WithExponentialBackoff(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(-1)));
        Assert.That(ex!.Message, Contains.Substring("Max exponential backoff cannot be negative"));
    }

    [Test]
    public void WithExponentialBackoff_MaxLessThanInitial_ThrowsArgumentException()
    {
        var ex = Assert.Throws<ArgumentException>(() => TryRetry.Create().WithExponentialBackoff(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(1)));
        Assert.That(ex!.Message, Contains.Substring("Max exponential backoff must be greater than or equal to initial delay"));
    }

    [Test]
    public void RetryOn_GenericValidExceptionType_SetsExceptionTypeSuccessfully()
    {
        var builder = TryRetry.Create().RetryOn<IOException>();
        Assert.That(builder, Is.Not.Null);
    }

    [Test]
    public void RetryOn_ArrayEmptyTypes_ThrowsArgumentException()
    {
        var ex = Assert.Throws<ArgumentException>(() => TryRetry.Create().RetryOn([]));
        Assert.That(ex!.Message, Contains.Substring("At least one exception type must be specified"));
    }

    [Test]
    public void RetryOn_ArrayNullTypes_ThrowsArgumentException()
    {
        var ex = Assert.Throws<ArgumentException>(() => TryRetry.Create().RetryOn(null!));
        Assert.That(ex!.Message, Contains.Substring("At least one exception type must be specified"));
    }

    [Test]
    public void RetryOn_NonExceptionType_ThrowsArgumentException()
    {
        var ex = Assert.Throws<ArgumentException>(() => TryRetry.Create().RetryOn(typeof(string)));
        Assert.That(ex!.Message, Contains.Substring("is not an Exception type"));
    }

    [Test]
    public void AlsoRetryOn_ValidExceptionType_AddsExceptionTypeSuccessfully()
    {
        var builder = TryRetry.Create().RetryOn<IOException>().AlsoRetryOn<ArgumentException>();
        Assert.That(builder, Is.Not.Null);
    }

    [Test]
    public void AlsoRetryOn_DuplicateExceptionType_DoesNotAddDuplicate()
    {
        var builder = TryRetry.Create().RetryOn<IOException>().AlsoRetryOn<IOException>();
        Assert.That(builder, Is.Not.Null); // No exception, just doesn't add duplicate
    }

    // Synchronous Execution Tests (Void Return)
    [Test]
    public void Execute_Void_SuccessOnFirstTry_ExecutesOnce()
    {
        var callCount = 0;
        var builder = TryRetry.Create().WithAttempts(3).WithDelay(TimeSpan.FromMilliseconds(10));

        builder.Execute(() => callCount++);

        Assert.That(callCount, Is.EqualTo(1));
    }

    [Test]
    public void Execute_Void_FailsAndRetries_SucceedsWithinAttempts()
    {
        var callCount = 0;
        var builder = TryRetry.Create().WithAttempts(3).WithDelay(TimeSpan.FromMilliseconds(10));

        builder.Execute(() =>
        {
            callCount++;
            if (callCount < 2)
            {
                throw new IOException("Test failure");
            }
        });

        Assert.That(callCount, Is.EqualTo(2));
    }

    [Test]
    public void Execute_Void_FailsAllAttempts_ThrowsFluentTryRetryException()
    {
        var callCount = 0;
        var builder = TryRetry.Create().WithAttempts(3).WithDelay(TimeSpan.FromMilliseconds(10));

        var ex = Assert.Throws<TryRetryException>(() =>
        {
            builder.Execute(() =>
            {
                callCount++;
                throw new IOException("Test failure");
            });
        });

        Assert.That(callCount, Is.EqualTo(3));
        Assert.That(ex!.Message, Contains.Substring("giving up after 3 attempt(s)"));
        Assert.That(ex!.InnerException, Is.TypeOf<IOException>());
    }

    [Test]
    public void Execute_Void_RetryOnSpecificException_DoesNotRetryOtherExceptions()
    {
        var callCount = 0;
        var builder = TryRetry.Create().WithAttempts(3).WithDelay(TimeSpan.FromMilliseconds(10)).RetryOn<IOException>();

        var ex = Assert.Throws<TryRetryException>(() =>
        {
            builder.Execute(() =>
            {
                callCount++;
                throw new ArgumentException("Test failure");
            });
        });

        Assert.That(callCount, Is.EqualTo(1)); // Should not retry since ArgumentException is not in retry list
        Assert.That(ex!.InnerException, Is.TypeOf<ArgumentException>());
    }

    [Test]
    public void Execute_Void_RetryOnBaseException_RetriesDerivedExceptions()
    {
        var callCount = 0;
        var builder = TryRetry.Create().WithAttempts(3).WithDelay(TimeSpan.FromMilliseconds(10)).RetryOn<IOException>();

        builder.Execute(() =>
        {
            callCount++;
            if (callCount < 2)
            {
                throw new FileNotFoundException("Test failure"); // Derived from IOException
            }
        });

        Assert.That(callCount, Is.EqualTo(2)); // Should retry since FileNotFoundException derives from IOException
    }

    // Synchronous Execution Tests (With Return Value)
    [Test]
    public void Execute_ReturnValue_SuccessOnFirstTry_ReturnsValue()
    {
        var builder = TryRetry.Create().WithAttempts(3).WithDelay(TimeSpan.FromMilliseconds(10));

        var result = builder.Execute(() => "Success");

        Assert.That(result, Is.EqualTo("Success"));
    }

    [Test]
    public void Execute_ReturnValue_FailsAndRetries_SucceedsWithinAttempts()
    {
        var callCount = 0;
        var builder = TryRetry.Create().WithAttempts(3).WithDelay(TimeSpan.FromMilliseconds(10));

        var result = builder.Execute(() =>
        {
            callCount++;
            if (callCount < 2)
            {
                throw new IOException("Test failure");
            }
            return "Success";
        });

        Assert.That(callCount, Is.EqualTo(2));
        Assert.That(result, Is.EqualTo("Success"));
    }

    [Test]
    public void Execute_ReturnValue_FailsAllAttempts_ThrowsFluentTryRetryException()
    {
        var callCount = 0;
        var builder = TryRetry.Create().WithAttempts(3).WithDelay(TimeSpan.FromMilliseconds(10));

        var ex = Assert.Throws<TryRetryException>(() =>
        {
            builder.Execute(() =>
            {
                callCount++;
                throw new IOException("Test failure");
            });
        });

        Assert.That(callCount, Is.EqualTo(3));
        Assert.That(ex!.Message, Contains.Substring("giving up after 3 attempt(s)"));
        Assert.That(ex!.InnerException, Is.TypeOf<IOException>());
    }

    [Test]
    public void Execute_ReturnValue_RetryOnSpecificException_DoesNotRetryOtherExceptions()
    {
        var callCount = 0;
        var builder = TryRetry.Create().WithAttempts(3).WithDelay(TimeSpan.FromMilliseconds(10)).RetryOn<IOException>();

        var ex = Assert.Throws<TryRetryException>(() =>
        {
            builder.Execute(() =>
            {
                callCount++;
                throw new ArgumentException("Test failure");
            });
        });

        Assert.That(callCount, Is.EqualTo(1)); // Should not retry since ArgumentException is not in retry list
        Assert.That(ex!.InnerException, Is.TypeOf<ArgumentException>());
    }

    [Test]
    public void Execute_ReturnValue_RetryOnBaseException_RetriesDerivedExceptions()
    {
        var callCount = 0;
        var builder = TryRetry.Create().WithAttempts(3).WithDelay(TimeSpan.FromMilliseconds(10)).RetryOn<IOException>();

        var result = builder.Execute(() =>
        {
            callCount++;
            if (callCount < 2)
            {
                throw new FileNotFoundException("Test failure"); // Derived from IOException
            }
            return "Success";
        });

        Assert.That(callCount, Is.EqualTo(2)); // Should retry since FileNotFoundException derives from IOException
        Assert.That(result, Is.EqualTo("Success"));
    }

    // Asynchronous Execution Tests (Void Return)
    [Test]
    public async Task ExecuteAsync_Void_SuccessOnFirstTry_ExecutesOnce()
    {
        var callCount = 0;
        var builder = TryRetry.Create().WithAttempts(3).WithDelay(TimeSpan.FromMilliseconds(10));

        await builder.ExecuteAsync(async _ =>
        {
            callCount++;
            await Task.CompletedTask;
        });

        Assert.That(callCount, Is.EqualTo(1));
    }

    [Test]
    public async Task ExecuteAsync_Void_FailsAndRetries_SucceedsWithinAttempts()
    {
        var callCount = 0;
        var builder = TryRetry.Create().WithAttempts(3).WithDelay(TimeSpan.FromMilliseconds(10));

        await builder.ExecuteAsync(async _ =>
        {
            callCount++;
            if (callCount < 2)
            {
                throw new IOException("Test failure");
            }
            await Task.CompletedTask;
        });

        Assert.That(callCount, Is.EqualTo(2));
    }

    [Test]
    public void ExecuteAsync_Void_FailsAllAttempts_ThrowsFluentTryRetryException()
    {
        var callCount = 0;
        var builder = TryRetry.Create().WithAttempts(3).WithDelay(TimeSpan.FromMilliseconds(10));

        var ex = Assert.ThrowsAsync<TryRetryException>(async () =>
        {
            await builder.ExecuteAsync(_ =>
            {
                callCount++;
                throw new IOException("Test failure");
            });
        });

        Assert.That(callCount, Is.EqualTo(3));
        Assert.That(ex!.Message, Contains.Substring("giving up after 3 attempt(s)"));
        Assert.That(ex!.InnerException, Is.TypeOf<IOException>());
    }

    [Test]
    public void ExecuteAsync_Void_RetryOnSpecificException_DoesNotRetryOtherExceptions()
    {
        var callCount = 0;
        var builder = TryRetry.Create().WithAttempts(3).WithDelay(TimeSpan.FromMilliseconds(10)).RetryOn<IOException>();

        var ex = Assert.ThrowsAsync<TryRetryException>(async () =>
        {
            await builder.ExecuteAsync(_ =>
            {
                callCount++;
                throw new ArgumentException("Test failure");
            });
        });

        Assert.That(callCount, Is.EqualTo(1)); // Should not retry since ArgumentException is not in retry list
        Assert.That(ex!.InnerException, Is.TypeOf<ArgumentException>());
    }

    [Test]
    public async Task ExecuteAsync_Void_RetryOnBaseException_RetriesDerivedExceptions()
    {
        var callCount = 0;
        var builder = TryRetry.Create().WithAttempts(3).WithDelay(TimeSpan.FromMilliseconds(10)).RetryOn<IOException>();

        await builder.ExecuteAsync(async _ =>
        {
            callCount++;
            if (callCount < 2)
            {
                throw new FileNotFoundException("Test failure"); // Derived from IOException
            }
            await Task.CompletedTask;
        });

        Assert.That(callCount, Is.EqualTo(2)); // Should retry since FileNotFoundException derives from IOException
    }

    // Asynchronous Execution Tests (With Return Value)
    [Test]
    public async Task ExecuteAsync_ReturnValue_SuccessOnFirstTry_ReturnsValue()
    {
        var builder = TryRetry.Create().WithAttempts(3).WithDelay(TimeSpan.FromMilliseconds(10));

        var result = await builder.ExecuteAsync(async _ =>
        {
            await Task.CompletedTask;
            return "Success";
        });

        Assert.That(result, Is.EqualTo("Success"));
    }

    [Test]
    public async Task ExecuteAsync_ReturnValue_FailsAndRetries_SucceedsWithinAttempts()
    {
        var callCount = 0;
        var builder = TryRetry.Create().WithAttempts(3).WithDelay(TimeSpan.FromMilliseconds(10));

        var result = await builder.ExecuteAsync(async _ =>
        {
            callCount++;
            if (callCount < 2)
            {
                throw new IOException("Test failure");
            }
            await Task.CompletedTask;
            return "Success";
        });

        Assert.That(callCount, Is.EqualTo(2));
        Assert.That(result, Is.EqualTo("Success"));
    }

    [Test]
    public void ExecuteAsync_ReturnValue_FailsAllAttempts_ThrowsFluentTryRetryException()
    {
        var callCount = 0;
        var builder = TryRetry.Create().WithAttempts(3).WithDelay(TimeSpan.FromMilliseconds(10));

        var ex = Assert.ThrowsAsync<TryRetryException>(async () =>
        {
            await builder.ExecuteAsync(_ =>
            {
                callCount++;
                throw new IOException("Test failure");
            });
        });

        Assert.That(callCount, Is.EqualTo(3));
        Assert.That(ex!.Message, Contains.Substring("giving up after 3 attempt(s)"));
        Assert.That(ex!.InnerException, Is.TypeOf<IOException>());
    }

    [Test]
    public void ExecuteAsync_ReturnValue_RetryOnSpecificException_DoesNotRetryOtherExceptions()
    {
        var callCount = 0;
        var builder = TryRetry.Create().WithAttempts(3).WithDelay(TimeSpan.FromMilliseconds(10)).RetryOn<IOException>();

        var ex = Assert.ThrowsAsync<TryRetryException>(async () =>
        {
            await builder.ExecuteAsync(_ =>
            {
                callCount++;
                throw new ArgumentException("Test failure");
            });
        });

        Assert.That(callCount, Is.EqualTo(1)); // Should not retry since ArgumentException is not in retry list
        Assert.That(ex!.InnerException, Is.TypeOf<ArgumentException>());
    }

    [Test]
    public async Task ExecuteAsync_ReturnValue_RetryOnBaseException_RetriesDerivedExceptions()
    {
        var callCount = 0;
        var builder = TryRetry.Create().WithAttempts(3).WithDelay(TimeSpan.FromMilliseconds(10)).RetryOn<IOException>();

        var result = await builder.ExecuteAsync(async _ =>
        {
            callCount++;
            if (callCount < 2)
            {
                throw new FileNotFoundException("Test failure"); // Derived from IOException
            }
            await Task.CompletedTask;
            return "Success";
        });

        Assert.That(callCount, Is.EqualTo(2)); // Should retry since FileNotFoundException derives from IOException
        Assert.That(result, Is.EqualTo("Success"));
    }

    // Asynchronous Execution Tests (Without Cancellation Token Overload)
    [Test]
    public async Task ExecuteAsync_Void_NoCancellationToken_SuccessOnFirstTry_ExecutesOnce()
    {
        var callCount = 0;
        var builder = TryRetry.Create().WithAttempts(3).WithDelay(TimeSpan.FromMilliseconds(10));

        await builder.ExecuteAsync(async () =>
        {
            callCount++;
            await Task.CompletedTask;
        });

        Assert.That(callCount, Is.EqualTo(1));
    }

    [Test]
    public async Task ExecuteAsync_ReturnValue_NoCancellationToken_SuccessOnFirstTry_ReturnsValue()
    {
        var builder = TryRetry.Create().WithAttempts(3).WithDelay(TimeSpan.FromMilliseconds(10));

        var result = await builder.ExecuteAsync(async () =>
        {
            await Task.CompletedTask;
            return "Success";
        });

        Assert.That(result, Is.EqualTo("Success"));
    }

    // Cancellation Tests
    [Test]
    public void Execute_Void_CancellationRequested_ThrowsOperationCanceledException()
    {
        var cts = new CancellationTokenSource();
        cts.Cancel();
        var builder = TryRetry.Create().WithCancellation(cts.Token);

        var ex = Assert.Throws<OperationCanceledException>(() => builder.Execute(() => { }));
        Assert.That(ex!.CancellationToken, Is.EqualTo(cts.Token));
    }

    [Test]
    public void Execute_ReturnValue_CancellationRequested_ThrowsOperationCanceledException()
    {
        var cts = new CancellationTokenSource();
        cts.Cancel();
        var builder = TryRetry.Create().WithCancellation(cts.Token);

        var ex = Assert.Throws<OperationCanceledException>(() => builder.Execute(() => "Success"));
        Assert.That(ex!.CancellationToken, Is.EqualTo(cts.Token));
    }

    [Test]
    public void ExecuteAsync_Void_CancellationRequested_ThrowsOperationCanceledException()
    {
        var cts = new CancellationTokenSource();
        cts.Cancel();
        var builder = TryRetry.Create().WithCancellation(cts.Token);

        var ex = Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await builder.ExecuteAsync(async _ => await Task.CompletedTask));
        Assert.That(ex!.CancellationToken, Is.EqualTo(cts.Token));
    }

    [Test]
    public void ExecuteAsync_ReturnValue_CancellationRequested_ThrowsOperationCanceledException()
    {
        var cts = new CancellationTokenSource();
        cts.Cancel();
        var builder = TryRetry.Create().WithCancellation(cts.Token);

        Assert.ThrowsAsync<TaskCanceledException>(async () =>
            await builder.ExecuteAsync(async ct =>
            {
                Assert.That(ct.IsCancellationRequested, Is.False); // Well, we never get this far...
                await Task.CompletedTask;
                return "Success";
            }));
    }

    // Delay Strategy Tests
    [Test]
    public void Execute_Void_WithFixedDelay_AppliesDelayBetweenRetries()
    {
        var callCount = 0;
        var delay = TimeSpan.FromMilliseconds(100);
        var builder = TryRetry.Create().WithAttempts(3).WithDelay(delay);
        var startTime = DateTime.UtcNow;

        builder.Execute(() =>
        {
            callCount++;
            if (callCount < 2)
            {
                throw new IOException("Test failure");
            }
        });

        var endTime = DateTime.UtcNow;
        var duration = endTime - startTime;
        Assert.That(callCount, Is.EqualTo(2));
        Assert.That(duration, Is.GreaterThanOrEqualTo(delay - TimeSpan.FromMilliseconds(20))); // Small margin for system variance
    }

    [Test]
    public void Execute_Void_WithRandomDelay_AppliesDelayInRange()
    {
        var callCount = 0;
        var minDelay = TimeSpan.FromMilliseconds(100);
        var maxDelay = TimeSpan.FromMilliseconds(200);
        var builder = TryRetry.Create().WithAttempts(3).WithRandomDelay(minDelay, maxDelay);
        var startTime = DateTime.UtcNow;

        builder.Execute(() =>
        {
            callCount++;
            if (callCount < 2)
            {
                throw new IOException("Test failure");
            }
        });

        var endTime = DateTime.UtcNow;
        var duration = endTime - startTime;
        Assert.That(callCount, Is.EqualTo(2));
        Assert.That(duration, Is.GreaterThanOrEqualTo(minDelay - TimeSpan.FromMilliseconds(20))); // Small margin
        Assert.That(duration, Is.LessThanOrEqualTo(maxDelay + TimeSpan.FromMilliseconds(50))); // Allow some margin
    }

    [Test]
    public void Execute_Void_WithExponentialBackoff_AppliesIncreasingDelay()
    {
        var callCount = 0;
        var initialDelay = TimeSpan.FromMilliseconds(100);
        var builder = TryRetry.Create().WithAttempts(3).WithExponentialBackoff(initialDelay);
        var startTime = DateTime.UtcNow;

        builder.Execute(() =>
        {
            callCount++;
            if (callCount < 3)
            {
                throw new IOException("Test failure");
            }
        });

        var endTime = DateTime.UtcNow;
        var duration = endTime - startTime;
        Assert.That(callCount, Is.EqualTo(3));
        // Rough check for exponential increase: initial + 2*initial
        Assert.That(duration, Is.GreaterThanOrEqualTo(initialDelay + initialDelay * 2 - TimeSpan.FromMilliseconds(50)));
    }

    [Test]
    public void Execute_Void_WithExponentialBackoffAndMax_RespectsMaxDelay()
    {
        var callCount = 0;
        var initialDelay = TimeSpan.FromMilliseconds(100);
        var maxDelay = TimeSpan.FromMilliseconds(200);
        var builder = TryRetry.Create().WithAttempts(5).WithExponentialBackoff(initialDelay, maxDelay);
        var startTime = DateTime.UtcNow;

        builder.Execute(() =>
        {
            callCount++;
            if (callCount < 5)
            {
                throw new IOException("Test failure");
            }
        });

        var endTime = DateTime.UtcNow;
        var duration = endTime - startTime;
        Assert.That(callCount, Is.EqualTo(5));
        // Total delay should not exceed sum of max delays (rough check with margin)
        Assert.That(duration, Is.LessThanOrEqualTo(maxDelay * 3 + initialDelay + TimeSpan.FromMilliseconds(100)));
    }

    // Logging Tests
    [Test]
    public void Execute_Void_WithLogger_LogsAttemptsAndSuccess()
    {
        var callCount = 0;
        var builder = TryRetry.Create().WithAttempts(3).WithDelay(TimeSpan.FromMilliseconds(10)).WithLogging(_loggerMock.Object);

        builder.Execute(() => callCount++);

        _loggerMock.Verify(l => l.Log(It.Is<LogLevel>(level => level == LogLevel.Trace),
            It.IsAny<EventId>(),
            It.IsAny<It.IsAnyType>(),
            It.IsAny<Exception>(),
            It.IsAny<Func<It.IsAnyType, Exception, string>>()), Times.Exactly(2)); // Start and success logs
    }

    [Test]
    public void Execute_Void_WithLogger_LogsRetriesAndFailures()
    {
        var builder = TryRetry.Create().WithAttempts(3).WithDelay(TimeSpan.FromMilliseconds(10)).WithLogging(_loggerMock.Object);

        Assert.Throws<TryRetryException>(() =>
        {
            builder.Execute(() => throw new IOException("Test failure"));
        });

        _loggerMock.Verify(l => l.Log(It.Is<LogLevel>(level => level == LogLevel.Trace),
            It.IsAny<EventId>(),
            It.IsAny<It.IsAnyType>(),
            It.IsAny<Exception>(),
            It.IsAny<Func<It.IsAnyType, Exception, string>>()), Times.AtLeast(3)); // Start attempts
        _loggerMock.Verify(l => l.Log(It.Is<LogLevel>(level => level == LogLevel.Warning),
            It.IsAny<EventId>(),
            It.IsAny<It.IsAnyType>(),
            It.IsAny<Exception>(),
            It.IsAny<Func<It.IsAnyType, Exception, string>>()), Times.Exactly(2)); // Retry warnings
        _loggerMock.Verify(l => l.Log(It.Is<LogLevel>(level => level == LogLevel.Error),
            It.IsAny<EventId>(),
            It.IsAny<It.IsAnyType>(),
            It.IsAny<Exception>(),
            It.IsAny<Func<It.IsAnyType, Exception, string>>()), Times.Once); // Final failure
    }

    [Test]
    public async Task ExecuteAsync_Void_WithLogger_LogsAttemptsAndSuccess()
    {
        var builder = TryRetry.Create().WithAttempts(3).WithDelay(TimeSpan.FromMilliseconds(10)).WithLogging(_loggerMock.Object);

        await builder.ExecuteAsync(async _ =>
        {
            await Task.CompletedTask;
        });

        _loggerMock.Verify(l => l.Log(It.Is<LogLevel>(level => level == LogLevel.Trace),
            It.IsAny<EventId>(),
            It.IsAny<It.IsAnyType>(),
            It.IsAny<Exception>(),
            It.IsAny<Func<It.IsAnyType, Exception, string>>()), Times.Exactly(2)); // Start and success logs
    }

    [Test]
    public void ExecuteAsync_Void_WithLogger_LogsRetriesAndFailures()
    {
        var builder = TryRetry.Create().WithAttempts(3).WithDelay(TimeSpan.FromMilliseconds(10)).WithLogging(_loggerMock.Object);

        Assert.ThrowsAsync<TryRetryException>(async () =>
        {
            await builder.ExecuteAsync(_ => throw new IOException("Test failure"));
        });

        _loggerMock.Verify(l => l.Log(It.Is<LogLevel>(level => level == LogLevel.Trace),
            It.IsAny<EventId>(),
            It.IsAny<It.IsAnyType>(),
            It.IsAny<Exception>(),
            It.IsAny<Func<It.IsAnyType, Exception, string>>()), Times.AtLeast(3)); // Start attempts
        _loggerMock.Verify(l => l.Log(It.Is<LogLevel>(level => level == LogLevel.Warning),
            It.IsAny<EventId>(),
            It.IsAny<It.IsAnyType>(),
            It.IsAny<Exception>(),
            It.IsAny<Func<It.IsAnyType, Exception, string>>()), Times.Exactly(2)); // Retry warnings
        _loggerMock.Verify(l => l.Log(It.Is<LogLevel>(level => level == LogLevel.Error),
            It.IsAny<EventId>(),
            It.IsAny<It.IsAnyType>(),
            It.IsAny<Exception>(),
            It.IsAny<Func<It.IsAnyType, Exception, string>>()), Times.Once); // Final failure
    }
}