using System;
using System.Collections.Generic;
using Odin.Core.Exceptions;

namespace Odin.Services.Certificate;

#nullable enable

/// <summary>
/// A terminal verdict from the CA: the order or one of its authorizations failed, and
/// immediately ordering again will fail the same way. Retrying is not just useless, it is
/// harmful - every retry is a fresh order, and Let's Encrypt counts failed authorizations
/// against a per-hostname hourly allowance.
/// </summary>
public class AcmeOrderException(string message, IReadOnlyCollection<string>? failedIdentifiers = null)
    : OdinSystemException(message)
{
    /// <summary>
    /// The names the CA actually complained about, where it told us. Empty when it did not.
    /// A certificate order is all-or-nothing, so knowing WHICH name failed is the difference
    /// between dropping one optional SAN and denying the identity every certificate it needs.
    /// </summary>
    public IReadOnlyCollection<string> FailedIdentifiers { get; } = failedIdentifiers ?? [];
}

//

/// <summary>
/// The CA is refusing orders for this name because a rate limit has been hit. Nothing will
/// change until <see cref="RetryAfter"/> has elapsed.
/// </summary>
public class AcmeRateLimitedException(
    string message,
    TimeSpan retryAfter,
    IReadOnlyCollection<string>? failedIdentifiers = null)
    : AcmeOrderException(message, failedIdentifiers)
{
    public TimeSpan RetryAfter { get; } = retryAfter;
}
