using System;

namespace Odin.Core.Exceptions;

/// <summary>
/// The payload or thumbnail the file header names is not in the store, and it is not coming: no payload
/// move is bringing it, and the header has not moved on to another version (#1913). HTTP 410 Gone.
/// </summary>
/// <remarks>
/// Neither cause can be told from the other at read time: a payload whose source never had it (a move
/// that accepted it as missing) or one the store lost. To the caller they are the same answer -- it will
/// not arrive, so show it as unavailable rather than retry -- and the server logs it for its operators.
/// <para>
/// Distinct from <see cref="OdinPayloadVersionGoneException"/>, where the file is fine and re-reading the
/// header gets the current version.
/// </para>
/// </remarks>
public class OdinPayloadGoneException : OdinClientException
{
    public OdinPayloadGoneException(string message, Exception innerException)
        : base(message, innerException)
    {
        ErrorCode = OdinClientErrorCode.PayloadGone;
    }
}
