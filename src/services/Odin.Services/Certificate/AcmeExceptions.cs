using System;
using Odin.Core.Exceptions;

namespace Odin.Services.Certificate;

#nullable enable

/// <summary>
/// A terminal verdict from the CA: the order or one of its authorizations failed, and
/// immediately ordering again will fail the same way. Retrying is not just useless, it is
/// harmful - every retry is a fresh order, and Let's Encrypt counts failed authorizations
/// against a per-hostname hourly allowance.
/// </summary>
public class AcmeOrderException(string message) : OdinSystemException(message);

//

/// <summary>
/// The CA is refusing orders for this name because a rate limit has been hit. Nothing will
/// change until <see cref="RetryAfter"/> has elapsed.
/// </summary>
public class AcmeRateLimitedException(string message, TimeSpan retryAfter) : AcmeOrderException(message)
{
    public TimeSpan RetryAfter { get; } = retryAfter;
}
