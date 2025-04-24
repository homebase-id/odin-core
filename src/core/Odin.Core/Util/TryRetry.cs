using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core.Exceptions;

namespace Odin.Core.Util;

#nullable enable

/// <summary>
/// Provides fluent retry functionality for operations that may fail temporarily.
/// </summary>
public static class TryRetry
{
    /// <summary>
    /// Creates a retry builder with default configuration (retries on any exception)
    /// </summary>
    public static RetryBuilder Create()
    {
        return new RetryBuilder();
    }

    /// <summary>
    /// Creates a retry builder configured to handle specific exception types
    /// </summary>
    public static RetryBuilder RetryOn<TException>() where TException : Exception
    {
        return new RetryBuilder().RetryOn<TException>();
    }

    /// <summary>
    /// Creates a retry builder configured to handle multiple exception types
    /// </summary>
    public static RetryBuilder RetryOn(params Type[] exceptionTypes)
    {
        return new RetryBuilder().RetryOn(exceptionTypes);
    }
}

/// <summary>
/// Provides a fluent API for retry operations with configurable behaviors
/// Note: Instances of this class should not be reused for multiple operations to avoid unintended configuration changes.
/// </summary>
public class RetryBuilder
{
    private readonly List<Type> _exceptionTypes = []; // Defaults to retry on all exceptions
    private int _attempts = 3;
    private TimeSpan? _exponentialBackoff = TimeSpan.FromMilliseconds(100);
    private TimeSpan? _maxExponentialBackoff;
    private TimeSpan? _delay;
    private ValueTuple<TimeSpan, TimeSpan>? _randomDelay;
    private CancellationToken _cancellationToken = CancellationToken.None;
    private ILogger? _logger;

    /// <summary>
    /// Sets the number of retry attempts
    /// </summary>
    public RetryBuilder WithAttempts(int attempts)
    {
        if (attempts < 1)
        {
            throw new ArgumentException("Attempts must be greater than 0");
        }
        _attempts = attempts;
        return this;
    }

    /// <summary>
    /// Sets a fixed delay between retries
    /// </summary>
    public RetryBuilder WithDelay(TimeSpan delay)
    {
        if (delay < TimeSpan.Zero)
        {
            throw new ArgumentException("Delay cannot be negative", nameof(delay));
        }
        _delay = delay;
        _randomDelay = null;
        _exponentialBackoff = null;
        _maxExponentialBackoff = null;
        return this;
    }

    /// <summary>
    /// Sets a random delay between min and max values between retries
    /// </summary>
    public RetryBuilder WithRandomDelay(TimeSpan min, TimeSpan max)
    {
        if (min < TimeSpan.Zero)
        {
            throw new ArgumentException("Delay cannot be negative", nameof(min));
        }
        if (min > max)
        {
            throw new ArgumentException("Minimum delay must be less than or equal to maximum delay");
        }
        _randomDelay = (min, max);
        _delay = null;
        _exponentialBackoff = null;
        _maxExponentialBackoff = null;
        return this;
    }

    /// <summary>
    /// Sets exponential backoff between retries with the given initial delay
    /// </summary>
    public RetryBuilder WithExponentialBackoff(TimeSpan initialDelay, TimeSpan? maxExponentialBackoff = null)
    {
        if (initialDelay < TimeSpan.Zero)
        {
            throw new ArgumentException("Initial delay must be greater than or equal to 0");
        }
        if (maxExponentialBackoff.HasValue)
        {
            if (maxExponentialBackoff.Value < TimeSpan.Zero)
            {
                throw new ArgumentException("Max exponential backoff cannot be negative",
                    nameof(maxExponentialBackoff));
            }
            if (maxExponentialBackoff.Value < initialDelay)
            {
                throw new ArgumentException("Max exponential backoff must be greater than or equal to initial delay",
                    nameof(maxExponentialBackoff));
            }
        }
        _exponentialBackoff = initialDelay;
        _maxExponentialBackoff = maxExponentialBackoff;
        _delay = null;
        _randomDelay = null;
        return this;
    }

    /// <summary>
    /// Sets the cancellation token for the retry operation
    /// </summary>
    public RetryBuilder WithCancellation(CancellationToken cancellationToken)
    {
        _cancellationToken = cancellationToken;
        return this;
    }

    /// <summary>
    /// Sets a logger to log retry attempts
    /// </summary>
    public RetryBuilder WithLogging(ILogger logger)
    {
        _logger = logger;
        return this;
    }

    /// <summary>
    /// Specifies a single exception type to retry on
    /// </summary>
    public RetryBuilder RetryOn<TException>() where TException : Exception
    {
        _exceptionTypes.Clear();
        _exceptionTypes.Add(typeof(TException));
        return this;
    }

    /// <summary>
    /// Specifies multiple exception types to retry on
    /// </summary>
    public RetryBuilder RetryOn(params Type[] exceptionTypes)
    {
        if (exceptionTypes == null || exceptionTypes.Length == 0)
        {
            throw new ArgumentException("At least one exception type must be specified");
        }

        foreach (var type in exceptionTypes)
        {
            if (!typeof(Exception).IsAssignableFrom(type))
            {
                throw new ArgumentException($"Type {type.Name} is not an Exception type");
            }
        }

        _exceptionTypes.Clear();
        _exceptionTypes.AddRange(exceptionTypes);
        return this;
    }

    /// <summary>
    /// Adds an additional exception type to retry on
    /// </summary>
    public RetryBuilder AlsoRetryOn<TException>() where TException : Exception
    {
        var exceptionType = typeof(TException);
        if (!_exceptionTypes.Contains(exceptionType))
        {
            _exceptionTypes.Add(exceptionType);
        }
        return this;
    }

    /// <summary>
    /// Executes the synchronous action with the configured retry behavior
    /// </summary>
    public void Execute(Action action)
    {
        ExecuteInternal(action);
    }

    /// <summary>
    /// Executes the synchronous action with the configured retry behavior
    /// </summary>
    public T Execute<T>(Func<T> action)
    {
        return ExecuteInternal(action)!; // Non-null return since T is expected
    }

    /// <summary>
    /// Executes the asynchronous action with the configured retry behavior
    /// </summary>
    public Task ExecuteAsync(Func<CancellationToken, Task> action)
    {
        return ExecuteInternalAsync(action);
    }

    /// <summary>
    /// Executes the asynchronous action with the configured retry behavior
    /// </summary>
    public Task ExecuteAsync(Func<Task> action)
    {
        return ExecuteInternalAsync(_ => action());
    }

    /// <summary>
    /// Executes the asynchronous action with the configured retry behavior
    /// </summary>
    public Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> action)
    {
        return ExecuteInternalAsync(action)
            .ContinueWith(t => t.Result!, TaskContinuationOptions.OnlyOnRanToCompletion);
    }

    /// <summary>
    /// Executes the asynchronous action with the configured retry behavior
    /// </summary>
    public Task<T> ExecuteAsync<T>(Func<Task<T>> action)
    {
        return ExecuteInternalAsync<T>(_ => action())
            .ContinueWith(t => t.Result!, TaskContinuationOptions.OnlyOnRanToCompletion);
    }

    // Wrapper for void synchronous action returning void
    private void ExecuteInternal(Action action)
    {
        ExecuteInternal<object>(() =>
        {
            action();
            return null!;
        });
    }

    // Consolidated synchronous internal method
    private T ExecuteInternal<T>(Func<T> action)
    {
        var attempt = 0;

        while (true)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            attempt++;

            try
            {
                _logger?.LogTrace("Executing attempt {attempt} of {maxAttempts}", attempt, _attempts);
                var result = action();
                _logger?.LogTrace("Attempt {attempt} succeeded", attempt);
                return result;
            }
            catch (Exception e) when (ShouldRetry(attempt, e))
            {
                var delayMs = CalculateDelay(attempt);
                _logger?.LogWarning(
                    "Attempt {attempt} of {maxAttempts} failed: '{message}' - retrying in {delayMs}ms",
                    attempt, _attempts, e.Message, delayMs);
                Thread.Sleep(delayMs);
            }
            catch (Exception e)
            {
                _logger?.LogWarning("All {attempts} retry attempts failed: '{message}'", _attempts, e.Message);
                throw new TryRetryException($"{e.Message} (giving up after {_attempts} attempt(s))", e);
            }
        }
    }

    //

    // Wrapper for void asynchronous action returning void
    private async Task ExecuteInternalAsync(Func<CancellationToken, Task> action)
    {
        await ExecuteInternalAsync<object>(async ct =>
        {
            await action(ct);
            return null!;
        });
    }

    //

    // Consolidated asynchronous internal method
    private async Task<T?> ExecuteInternalAsync<T>(Func<CancellationToken, Task<T>> action)
    {
        var attempt = 0;

        while (true)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            attempt++;

            try
            {
                _logger?.LogTrace("Executing attempt {attempt} of {maxAttempts}", attempt, _attempts);
                var result = await action(_cancellationToken);
                _logger?.LogTrace("Attempt {attempt} succeeded", attempt);
                return result;
            }
            catch (Exception e) when (ShouldRetry(attempt, e))
            {
                var delayMs = CalculateDelay(attempt);
                _logger?.LogWarning(
                    "Attempt {attempt} of {maxAttempts} failed: '{message}' - retrying in {delayMs}ms",
                    attempt, _attempts, e.Message, delayMs);
                await Task.Delay(delayMs, _cancellationToken);
            }
            catch (Exception e)
            {
                _logger?.LogWarning("All {attempts} retry attempts failed: '{message}'", _attempts, e.Message);
                throw new TryRetryException($"{e.Message} (giving up after {_attempts} attempt(s))", e);
            }
        }
    }

    //

    private bool ShouldRetry(int currentAttempt, Exception exception)
    {
        return
            currentAttempt < _attempts &&
            (_exceptionTypes.Count == 0 || _exceptionTypes.Any(type => type.IsAssignableFrom(exception.GetType())));
    }

    //

    private int CalculateDelay(int attempt)
    {
        if (_delay.HasValue)
        {
            return (int)_delay.Value.TotalMilliseconds;
        }

        if (_randomDelay.HasValue)
        {
            var (min, max) = _randomDelay.Value;
            return Random.Shared.Next((int)min.TotalMilliseconds, (int)max.TotalMilliseconds);
        }

        // Default to exponential backoff
        var multiplier = Math.Pow(2, attempt - 1);
        var delayMs = multiplier * _exponentialBackoff!.Value.TotalMilliseconds;
        var delay = (long)delayMs;
        if (_maxExponentialBackoff.HasValue)
        {
            delay = Math.Min(delay, (long)_maxExponentialBackoff.Value.TotalMilliseconds);
        }
        return (int)Math.Min(delay, int.MaxValue);
    }
}

public class TryRetryException(string message, Exception innerException)
    : OdinSystemException(message, innerException);
