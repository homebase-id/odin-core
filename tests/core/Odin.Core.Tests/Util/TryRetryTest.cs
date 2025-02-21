using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using NUnit.Framework.Legacy;
using Odin.Core.Util;

namespace Odin.Core.Tests.Util;

public class TryRetryTest
{
    #region WithDelay

    //
    // WithDelay ...
    //

    [Test]
    public void SyncWithDelayReturningVoidShouldSucceed()
    {
        // Arrange
        var ts = Stopwatch.StartNew();

        // Act
        var attempts = TryRetry.WithDelay(1, TimeSpan.FromMilliseconds(100), CancellationToken.None, () =>
        {
            Thread.Sleep(1);
        });

        // Assert
        ClassicAssert.AreEqual(1, attempts);
        Assert.That(ts.ElapsedMilliseconds, Is.LessThan(100));
    }

    [Test]
    public async Task AsyncWithDelayReturningVoidShouldSucceed()
    {
        // Arrange
        var ts = Stopwatch.StartNew();

        // Act
        var attempts = await TryRetry.WithDelayAsync(1, TimeSpan.FromMilliseconds(100), CancellationToken.None, async () =>
        {
            await Task.Delay(1);
        });

        // Assert
        ClassicAssert.AreEqual(1, attempts);
        Assert.That(ts.ElapsedMilliseconds, Is.LessThan(100));
    }

    //

    [Test]
    public void SyncWithDelayReturningIntShouldSucceed()
    {
        // Arrange
        var result = 0;
        var ts = Stopwatch.StartNew();

        // Act
        var attempts = TryRetry.WithDelay(10, TimeSpan.FromMilliseconds(100), CancellationToken.None, () =>
        {
            result += 42;
        });

        // Assert
        ClassicAssert.AreEqual(1, attempts);
        ClassicAssert.AreEqual(42, result);
        Assert.That(ts.ElapsedMilliseconds, Is.LessThan(100));
    }

    [Test]
    public async Task AsyncWithDelayReturningIntShouldSucceed()
    {
        // Arrange
        var result = 0;
        var ts = Stopwatch.StartNew();

        // Act
        var attempts = await TryRetry.WithDelayAsync(10, TimeSpan.FromMilliseconds(100), CancellationToken.None, async () =>
        {
            await Task.Delay(1);
            result = 42;
        });

        // Assert
        ClassicAssert.AreEqual(1, attempts);
        ClassicAssert.AreEqual(42, result);
        Assert.That(ts.ElapsedMilliseconds, Is.LessThan(100));
    }

    //

    [Test]
    public void SyncWithDelayShouldRetryAndSuceed()
    {
        // Arrange
        var attempt = 0;
        var ts = Stopwatch.StartNew();

        // Act
        var attempts = TryRetry.WithDelay(10, TimeSpan.FromMilliseconds(100), CancellationToken.None, () =>
        {
            attempt++;
            if (attempt < 2)
            {
                throw new Exception("oh no");
            }
        });

        // Assert

        ClassicAssert.AreEqual(attempt, attempts);
        Assert.That(ts.ElapsedMilliseconds, Is.GreaterThanOrEqualTo(100));
    }

    [Test]
    public async Task AsyncWithDelayShouldRetryAndSucceed()
    {
        // Arrange
        var attempt = 0;
        var ts = Stopwatch.StartNew();

        // Act
        var attempts = await TryRetry.WithDelayAsync(10, TimeSpan.FromMilliseconds(100), CancellationToken.None, async () =>
        {
            attempt++;
            await Task.Delay(1);
            if (attempt < 3)
            {
                throw new Exception("oh no");
            }
        });

        // Assert
        ClassicAssert.AreEqual(attempt, attempts);
        Assert.That(ts.ElapsedMilliseconds, Is.GreaterThanOrEqualTo(200));
    }

    [Test]
    public async Task AsyncWithDelayShouldRetryAndSucceedWithoutAsyncAwaitKeywords()
    {
        // Arrange
        var attempt = 0;
        var ts = Stopwatch.StartNew();

        async Task Operation()
        {
            attempt++;
            await Task.Delay(1);
            if (attempt < 3)
            {
                throw new Exception("oh no");
            }
        }

        // Act
        var attempts = await TryRetry.WithDelayAsync(10, TimeSpan.FromMilliseconds(100), CancellationToken.None, Operation);

        // Assert
        ClassicAssert.AreEqual(attempt, attempts);
        Assert.That(ts.ElapsedMilliseconds, Is.GreaterThanOrEqualTo(200));
    }

    //

    [Test]
    public void SyncWithDelayShouldRetryAndFail()
    {
        // Arrange
        var attempt = 0;
        var ts = Stopwatch.StartNew();

        // Act
        var exception = Assert.Throws<TryRetryException>(() =>
        {
            TryRetry.WithDelay(3, TimeSpan.FromMilliseconds(100), CancellationToken.None, () =>
            {
                attempt++;
                throw new Exception("oh no");
            });
        });

        // Assert
        ClassicAssert.AreEqual(3, attempt);
        Assert.That(ts.ElapsedMilliseconds, Is.GreaterThanOrEqualTo(200));
#if !CI_GITHUB
        Assert.That(ts.ElapsedMilliseconds, Is.LessThan(300));
#endif
        Assert.That(exception?.Message, Is.EqualTo("oh no (giving up after 3 attempt(s))"));
        Assert.That(exception.InnerException?.Message, Is.EqualTo("oh no"));
    }

    [Test]
    public void AsyncWithDelayShouldRetryAndFail()
    {
        // Arrange
        var attempt = 0;
        var ts = Stopwatch.StartNew();

        // Act
        var exception = Assert.ThrowsAsync<TryRetryException>(async () =>
        {
            await TryRetry.WithDelayAsync(3, TimeSpan.FromMilliseconds(100), CancellationToken.None, async () =>
            {
                attempt++;
                await Task.Delay(1);
                throw new Exception("oh no");
            });
        });

        // Assert
        ClassicAssert.AreEqual(3, attempt);
        Assert.That(ts.ElapsedMilliseconds, Is.GreaterThanOrEqualTo(200));
#if !CI_GITHUB
        Assert.That(ts.ElapsedMilliseconds, Is.LessThan(300));
#endif
        Assert.That(exception?.Message, Is.EqualTo("oh no (giving up after 3 attempt(s))"));
        Assert.That(exception.InnerException?.Message, Is.EqualTo("oh no"));
    }

    [Test]
    public void AsyncWithDelayShouldRetryAndFailWithoutAsyncAwaitKeywords()
    {
        // Arrange
        var attempt = 0;
        var ts = Stopwatch.StartNew();

        async Task Operation()
        {
            attempt++;
            await Task.Delay(1);
            throw new Exception("oh no");
        }

        // Act
        var exception = Assert.ThrowsAsync<TryRetryException>(async () =>
        {
            await TryRetry.WithDelayAsync(3, TimeSpan.FromMilliseconds(100), CancellationToken.None, Operation);
        });

        // Assert
        ClassicAssert.AreEqual(3, attempt);
        Assert.That(ts.ElapsedMilliseconds, Is.GreaterThanOrEqualTo(200));
#if !CI_GITHUB
        Assert.That(ts.ElapsedMilliseconds, Is.LessThan(300));
#endif
        Assert.That(exception?.Message, Is.EqualTo("oh no (giving up after 3 attempt(s))"));
        Assert.That(exception.InnerException?.Message, Is.EqualTo("oh no"));
    }

    //

    [Test]
    public void SyncWithDelayShouldRetryAndFailOnSpecificExceptionType()
    {
        // Arrange
        var attempt = 0;
        var ts = Stopwatch.StartNew();

        // Act
        var exception = Assert.Throws<TryRetryException>(() =>
        {
            TryRetry.WithDelay<ArgumentException>(3, TimeSpan.FromMilliseconds(100), CancellationToken.None, () =>
            {
                attempt++;
                throw new ArgumentException("oh no");
            });
        });

        // Assert
        ClassicAssert.AreEqual(3, attempt);
        Assert.That(ts.ElapsedMilliseconds, Is.GreaterThanOrEqualTo(200));
#if !CI_GITHUB
        Assert.That(ts.ElapsedMilliseconds, Is.LessThan(300));
#endif
        Assert.That(exception?.Message, Is.EqualTo("oh no (giving up after 3 attempt(s))"));
        Assert.That(exception.InnerException?.Message, Is.EqualTo("oh no"));
        Assert.That(exception.InnerException.GetType(), Is.EqualTo(typeof(ArgumentException)));
    }

    [Test]
    public void AsyncWithDelayShouldRetryAndFailOnSpecificExceptionType()
    {
        // Arrange
        var attempt = 0;
        var ts = Stopwatch.StartNew();

        // Act
        var exception = Assert.ThrowsAsync<TryRetryException>(async () =>
        {
            await TryRetry.WithDelayAsync<ArgumentException>(3, TimeSpan.FromMilliseconds(100), CancellationToken.None, async () =>
            {
                attempt++;
                await Task.Delay(1);
                throw new ArgumentException("oh no");
            });
        });

        // Assert
        ClassicAssert.AreEqual(3, attempt);
        Assert.That(ts.ElapsedMilliseconds, Is.GreaterThanOrEqualTo(200));
#if !CI_GITHUB
        Assert.That(ts.ElapsedMilliseconds, Is.LessThan(300));
#endif
        Assert.That(exception?.Message, Is.EqualTo("oh no (giving up after 3 attempt(s))"));
        Assert.That(exception.InnerException?.Message, Is.EqualTo("oh no"));
        Assert.That(exception.InnerException.GetType(), Is.EqualTo(typeof(ArgumentException)));
    }

    //

    [Test]
    public void SyncWithDelayShouldSkipRetryOnWrongExceptionType()
    {
        // Arrange
        var attempt = 0;
        var ts = Stopwatch.StartNew();

        // Act
        var exception = Assert.Throws<InvalidOperationException>(() =>
        {
            TryRetry.WithDelay<ArgumentException>(3, TimeSpan.FromMilliseconds(100), CancellationToken.None, () =>
            {
                attempt++;
                throw new InvalidOperationException("oh no");
            });
        });

        // Assert
        ClassicAssert.AreEqual(1, attempt);
        Assert.That(ts.ElapsedMilliseconds, Is.LessThan(100));
        Assert.That(exception?.Message, Is.EqualTo("oh no"));
        Assert.That(exception.InnerException, Is.Null);
    }

    [Test]
    public void AsyncWithDelayShouldSkipRetryOnWrongExceptionType()
    {
        // Arrange
        var attempt = 0;
        var ts = Stopwatch.StartNew();

        // Act
        var exception = Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await TryRetry.WithDelayAsync<ArgumentException>(3, TimeSpan.FromMilliseconds(100), CancellationToken.None, async () =>
            {
                attempt++;
                await Task.Delay(1);
                throw new InvalidOperationException("oh no");
            });
        });

        // Assert
        ClassicAssert.AreEqual(1, attempt);
        Assert.That(ts.ElapsedMilliseconds, Is.LessThan(100));
        Assert.That(exception?.Message, Is.EqualTo("oh no"));
        Assert.That(exception.InnerException, Is.Null);
    }

    //

    #endregion

    #region WithBackoff

    //
    // WithBackoff ...
    //

    [Test]
    public void SyncWithBackoffReturningVoidShouldSucceed()
    {
        // Arrange
        var ts = Stopwatch.StartNew();

        // Act
        var attempts = TryRetry.WithBackoff(10, TimeSpan.FromMilliseconds(100), CancellationToken.None, () =>
        {
            Thread.Sleep(1);
        });

        // Assert
        ClassicAssert.AreEqual(1, attempts);
        Assert.That(ts.ElapsedMilliseconds, Is.LessThan(100));
    }

    [Test]
    public async Task AsyncWithBackoffReturningVoidShouldSucceed()
    {
        // Arrange
        var ts = Stopwatch.StartNew();

        // Act
        var attempts = await TryRetry.WithBackoffAsync(10, TimeSpan.FromMilliseconds(100), CancellationToken.None, async () =>
        {
            await Task.Delay(1);
        });

        // Assert
        ClassicAssert.AreEqual(1, attempts);
        Assert.That(ts.ElapsedMilliseconds, Is.LessThan(100));
    }

    //

    [Test]
    public void SyncWithBackoffReturningIntShouldSucceed()
    {
        // Arrange
        var result = 0;
        var ts = Stopwatch.StartNew();

        // Act
        var attempts = TryRetry.WithBackoff(10, TimeSpan.FromMilliseconds(100), CancellationToken.None, () =>
        {
            result = 42;
        });

        // Assert
        ClassicAssert.AreEqual(1, attempts);
        ClassicAssert.AreEqual(42, result);
        Assert.That(ts.ElapsedMilliseconds, Is.LessThan(100));
    }

    [Test]
    public async Task AsyncWithBackoffReturningIntShouldSucceed()
    {
        // Arrange
        var result = 0;
        var ts = Stopwatch.StartNew();

        // Act
        var attempts = await TryRetry.WithBackoffAsync(10, TimeSpan.FromMilliseconds(100), CancellationToken.None, async () =>
        {
            await Task.Delay(1);
            result = 42;
        });

        // Assert
        ClassicAssert.AreEqual(1, attempts);
        ClassicAssert.AreEqual(42, result);
        Assert.That(ts.ElapsedMilliseconds, Is.LessThan(100));
    }

    //

    [Test]
    public void SyncWithBackoffShouldRetryAndSuceed()
    {
        // Arrange
        var attempt = 0;
        var ts = Stopwatch.StartNew();

        // Act
        var attempts = TryRetry.WithBackoff(10, TimeSpan.FromMilliseconds(100), CancellationToken.None, () =>
        {
            attempt++;
            if (attempt < 4)
            {
                throw new Exception("oh no");
            }
        });

        // Assert
        ClassicAssert.AreEqual(attempt, attempts);
        Assert.That(ts.ElapsedMilliseconds, Is.GreaterThanOrEqualTo(700));
#if !CI_GITHUB
        Assert.That(ts.ElapsedMilliseconds, Is.LessThan(850));
#endif
    }

    [Test]
    public async Task AsyncWithBackoffShouldRetryAndSucceed()
    {
        // Arrange
        var attempt = 0;
        var ts = Stopwatch.StartNew();

        // Act
        var attempts = await TryRetry.WithBackoffAsync(10, TimeSpan.FromMilliseconds(100), CancellationToken.None, async () =>
        {
            attempt++;
            await Task.Delay(1);
            if (attempt < 4)
            {
                throw new Exception("oh no");
            }
        });

        // Assert
        ClassicAssert.AreEqual(attempt, attempts);
        Assert.That(ts.ElapsedMilliseconds, Is.GreaterThanOrEqualTo(700));
#if !CI_GITHUB
        Assert.That(ts.ElapsedMilliseconds, Is.LessThan(850));
#endif
    }

    [Test]
    public async Task AsyncWithBackoffShouldRetryAndSucceedWithoutAsyncAwaitKeywords()
    {
        // Arrange
        var attempt = 0;
        var ts = Stopwatch.StartNew();

        async Task Operation()
        {
            attempt++;
            await Task.Delay(1);
            if (attempt < 4)
            {
                throw new Exception("oh no");
            }
        }

        // Act
        var attempts = await TryRetry.WithBackoffAsync(10, TimeSpan.FromMilliseconds(100), CancellationToken.None, Operation);

        // Assert
        ClassicAssert.AreEqual(attempt, attempts);
        Assert.That(ts.ElapsedMilliseconds, Is.GreaterThanOrEqualTo(700));
#if !CI_GITHUB
        Assert.That(ts.ElapsedMilliseconds, Is.LessThan(850));
#endif
    }

    //

    [Test]
    public void SyncWithBackoffShouldRetryAndFail()
    {
        // Arrange
        var attempt = 0;
        var ts = Stopwatch.StartNew();

        // Act
        var exception = Assert.Throws<TryRetryException>(() =>
        {
            TryRetry.WithBackoff(4, TimeSpan.FromMilliseconds(100), CancellationToken.None, () =>
            {
                attempt++;
                throw new Exception("oh no");
            });
        });

        // Assert
        ClassicAssert.AreEqual(4, attempt);
        Assert.That(ts.ElapsedMilliseconds, Is.GreaterThanOrEqualTo(700));
#if !CI_GITHUB
        Assert.That(ts.ElapsedMilliseconds, Is.LessThan(850));
#endif
        Assert.That(exception?.Message, Is.EqualTo("oh no (giving up after 4 attempt(s))"));
        Assert.That(exception.InnerException?.Message, Is.EqualTo("oh no"));
    }

    [Test]
    public void AsyncWithBackoffShouldRetryAndFail()
    {
        // Arrange
        var attempt = 0;
        var ts = Stopwatch.StartNew();

        // Act
        var exception = Assert.ThrowsAsync<TryRetryException>(async () =>
        {
            await TryRetry.WithBackoffAsync(4, TimeSpan.FromMilliseconds(100), CancellationToken.None, async () =>
            {
                attempt++;
                await Task.Delay(1);
                throw new Exception("oh no");
            });
        });

        // Assert
        ClassicAssert.AreEqual(4, attempt);
        Assert.That(ts.ElapsedMilliseconds, Is.GreaterThanOrEqualTo(700));
#if !CI_GITHUB
        Assert.That(ts.ElapsedMilliseconds, Is.LessThan(850));
#endif
        Assert.That(exception?.Message, Is.EqualTo("oh no (giving up after 4 attempt(s))"));
        Assert.That(exception.InnerException?.Message, Is.EqualTo("oh no"));
    }

    [Test]
    public void AsyncWithBackoffShouldRetryAndFailWithoutAsyncAwaitKeywords()
    {
        // Arrange
        var attempt = 0;
        var ts = Stopwatch.StartNew();

        async Task Operation()
        {
            attempt++;
            await Task.Delay(1);
            throw new Exception("oh no");
        }

        // Act
        var exception = Assert.ThrowsAsync<TryRetryException>(async () =>
        {
            await TryRetry.WithBackoffAsync(4, TimeSpan.FromMilliseconds(100), CancellationToken.None, Operation);
        });

        // Assert
        ClassicAssert.AreEqual(4, attempt);
        Assert.That(ts.ElapsedMilliseconds, Is.GreaterThanOrEqualTo(700));
#if !CI_GITHUB
        Assert.That(ts.ElapsedMilliseconds, Is.LessThan(850));
#endif
        Assert.That(exception?.Message, Is.EqualTo("oh no (giving up after 4 attempt(s))"));
        Assert.That(exception.InnerException?.Message, Is.EqualTo("oh no"));
    }

    //

    [Test]
    public void SyncWithBackoffShouldRetryAndFailOnSpecificExceptionType()
    {
        // Arrange
        var attempt = 0;
        var ts = Stopwatch.StartNew();

        // Act
        var exception = Assert.Throws<TryRetryException>(() =>
        {
            TryRetry.WithBackoff<ArgumentException>(3, TimeSpan.FromMilliseconds(100), CancellationToken.None, () =>
            {
                attempt++;
                throw new ArgumentException("oh no");
            });
        });

        // Assert
        ClassicAssert.AreEqual(3, attempt);
        Assert.That(ts.ElapsedMilliseconds, Is.GreaterThanOrEqualTo(300));
#if !CI_GITHUB
        Assert.That(ts.ElapsedMilliseconds, Is.LessThan(400));
#endif
        Assert.That(exception?.Message, Is.EqualTo("oh no (giving up after 3 attempt(s))"));
        Assert.That(exception.InnerException?.Message, Is.EqualTo("oh no"));
        Assert.That(exception.InnerException.GetType(), Is.EqualTo(typeof(ArgumentException)));
    }

    [Test]
    public void AsyncWithBAckoffShouldRetryAndFailOnSpecificExceptionType()
    {
        // Arrange
        var attempt = 0;
        var ts = Stopwatch.StartNew();

        // Act
        var exception = Assert.ThrowsAsync<TryRetryException>(async () =>
        {
            await TryRetry.WithBackoffAsync<ArgumentException>(3, TimeSpan.FromMilliseconds(100), CancellationToken.None, async () =>
            {
                attempt++;
                await Task.Delay(1);
                throw new ArgumentException("oh no");
            });
        });

        // Assert
        ClassicAssert.AreEqual(3, attempt);
        Assert.That(ts.ElapsedMilliseconds, Is.GreaterThanOrEqualTo(300));
#if !CI_GITHUB
        Assert.That(ts.ElapsedMilliseconds, Is.LessThan(400));
#endif
        Assert.That(exception?.Message, Is.EqualTo("oh no (giving up after 3 attempt(s))"));
        Assert.That(exception.InnerException?.Message, Is.EqualTo("oh no"));
        Assert.That(exception.InnerException.GetType(), Is.EqualTo(typeof(ArgumentException)));
    }

    //

    [Test]
    public void SyncWithBackoffShouldSkipRetryOnWrongExceptionType()
    {
        // Arrange
        var attempt = 0;
        var ts = Stopwatch.StartNew();

        // Act
        var exception = Assert.Throws<InvalidOperationException>(() =>
        {
            TryRetry.WithBackoff<ArgumentException>(3, TimeSpan.FromMilliseconds(100), CancellationToken.None, () =>
            {
                attempt++;
                throw new InvalidOperationException("oh no");
            });
        });

        // Assert
        ClassicAssert.AreEqual(1, attempt);
        Assert.That(ts.ElapsedMilliseconds, Is.LessThan(100));
        Assert.That(exception?.Message, Is.EqualTo("oh no"));
        Assert.That(exception.InnerException, Is.Null);
    }

    [Test]
    public void AsyncWithBackoffShouldSkipRetryOnWrongExceptionType()
    {
        // Arrange
        var attempt = 0;
        var ts = Stopwatch.StartNew();

        // Act
        var exception = Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await TryRetry.WithBackoffAsync<ArgumentException>(3, TimeSpan.FromMilliseconds(100), CancellationToken.None, async () =>
            {
                attempt++;
                await Task.Delay(1);
                throw new InvalidOperationException("oh no");
            });
        });

        // Assert
        ClassicAssert.AreEqual(1, attempt);
        Assert.That(ts.ElapsedMilliseconds, Is.LessThan(100));
        Assert.That(exception?.Message, Is.EqualTo("oh no"));
        Assert.That(exception.InnerException, Is.Null);
    }

    //

    #endregion

}