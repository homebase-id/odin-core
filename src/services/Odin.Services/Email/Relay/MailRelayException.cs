using Odin.Core.Exceptions;

#nullable enable

namespace Odin.Services.Email.Relay;

/// <summary>
/// The relay answered, and the answer was no. Carries the HTTP status so callers can tell a
/// refusal that will repeat (plan limit, bad request) from a blip worth retrying - the
/// message alone cannot say which without matching on the vendor's English.
/// </summary>
public class MailRelayException(string message, int? statusCode) : OdinSystemException(message)
{
    public int? StatusCode { get; } = statusCode;

    /// <summary>
    /// A 4xx: the relay understood the request and refused it, so asking again changes
    /// nothing until a person changes something (e.g. the 2026-10-07 free-plan sender cap).
    /// A 5xx stays transient - SMTP2GO answers an unknown API key with a 500, and a key
    /// mid-rotation is exactly what a retry should ride out. So do the two 4xx that mean
    /// "not now" rather than "no": 408 (timeout) and 429 (rate limit, e.g. a bulk backfill).
    /// </summary>
    public bool IsPermanent => StatusCode is >= 400 and < 500 and not 408 and not 429;
}
