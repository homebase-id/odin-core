using System;
using System.Diagnostics;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core.Util;

namespace Odin.Core.Tests.Util;

public class RetryUtilTest
{
    [Test]
    public void SyncRetryShouldSucceed()
    {
        // Arrange
        string Operation()
        {
            return "success!";
        }

        // Act
        var result = RetryUtil.Retry(
            operation: Operation,
            maxRetryCount: 3,
            delayBetweenRetries: TimeSpan.FromMilliseconds(200),
            out var attempts
        );

        // Assert
        Assert.AreEqual(result, "success!");
        Assert.AreEqual(1, attempts);
    }

    //

    [Test]
    public async Task AsyncRetryShouldSucceed()
    {
        // Arrange
        async Task<string> Operation()
        {
            await Task.Delay(1);
            return "success!";
        }

        // Act
        var result = await RetryUtil.RetryAsync(
            operation: Operation,
            maxRetryCount: 3,
            delayBetweenRetries: TimeSpan.FromMilliseconds(200)
        );

        // Assert
        Assert.AreEqual(result, "success!");
    }

    //

    [Test]
    public void SyncRetryShouldFailAndSucceed()
    {
        var count = 0;

        // Arrange
        string Operation()
        {
            if (count == 0)
            {
                count++;
                throw new Exception("oh no!");
            }
            return "success!";
        }

        // Act
        var ts = Stopwatch.StartNew();
        var result = RetryUtil.Retry(
            operation: Operation,
            maxRetryCount: 3,
            delayBetweenRetries: TimeSpan.FromMilliseconds(200),
            out var attempts
        );

        // Assert
        Assert.AreEqual(result, "success!");
        Assert.AreEqual(2, attempts);
        Assert.That(ts.ElapsedMilliseconds, Is.GreaterThanOrEqualTo(200));
    }

    //

    [Test]
    public async Task AsyncRetryShouldFailAndSucceed()
    {
        var count = 0;

        // Arrange
        async Task<string> Operation()
        {
            await Task.Delay(1);
            if (count == 0)
            {
                count++;
                throw new Exception("oh no!");
            }
            return "success!";
        }

        // Act
        var ts = Stopwatch.StartNew();
        var result = await RetryUtil.RetryAsync(
            operation: Operation,
            maxRetryCount: 3,
            delayBetweenRetries: TimeSpan.FromMilliseconds(200)
        );

        // Assert
        Assert.AreEqual(result, "success!");
        Assert.That(ts.ElapsedMilliseconds, Is.GreaterThanOrEqualTo(200));
    }

    //

    [Test]
    public void SyncRetryShouldGiveUpAndThrow()
    {
        // Arrange
        string Operation()
        {
            throw new ArgumentException("oh no!");
        }

        // Act
        var ts = Stopwatch.StartNew();
        var attempts = 0;
        var exception = Assert.Throws<RetryUtilException>(() =>
        {
            RetryUtil.Retry(
                operation: Operation,
                maxRetryCount: 3,
                delayBetweenRetries: TimeSpan.FromMilliseconds(200),
                out attempts);
        });

        // Assert
        Assert.AreEqual(3, attempts);
        Assert.IsTrue(exception?.Message.Contains("Failed to execute operation after 3 attempts"));
        Assert.IsTrue(exception?.InnerException?.Message.Contains("oh no!"));
        Assert.That(ts.ElapsedMilliseconds, Is.GreaterThanOrEqualTo(400));
    }

    //

    [Test]
    public void AsyncRetryShouldGiveUpAndThrow()
    {
        // Arrange
        async Task<string> Operation()
        {
            await Task.Delay(1);
            throw new ArgumentException("oh no!");
        }

        // Act
        var ts = Stopwatch.StartNew();
        var exception = Assert.ThrowsAsync<RetryUtilException>(async () =>
        {
            await RetryUtil.RetryAsync(
                operation: Operation,
                maxRetryCount: 3,
                delayBetweenRetries: TimeSpan.FromMilliseconds(400)
            );
        });

        // Assert
        Assert.IsTrue(exception?.Message.Contains("Failed to execute operation after 3 attempts"));
        Assert.IsTrue(exception?.InnerException?.Message.Contains("oh no!"));
        Assert.That(ts.ElapsedMilliseconds, Is.GreaterThanOrEqualTo(400));
    }

    //

    [Test]
    public void NO_DONT_UseSyncRetryWithAsyncOperation()
    {
        // !!! DONT use Sync Retry With Async Operation. It cannot catch exceptions and will simply throw them.

        var count = 0;

        // Arrange
        async Task<string> Operation()
        {
            await Task.Delay(1);
            if (count == 0)
            {
                count++;
                throw new ArgumentException("oh no!");
            }
            return "success!";
        }

        // Act - OH NO this will always throw! Notice it's throwing the original exception, not a RetryUtilException
        Assert.ThrowsAsync<ArgumentException>(async () =>
        {
            await RetryUtil.Retry(
                operation: Operation,
                maxRetryCount: 3,
                delayBetweenRetries: TimeSpan.FromMilliseconds(200),
                out _);
        });
    }

}