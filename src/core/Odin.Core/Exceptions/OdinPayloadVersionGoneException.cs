using System;

namespace Odin.Core.Exceptions;

/// <summary>
/// The payload or thumbnail file a caller resolved is gone because that payload has since been
/// replaced. The version asked for no longer exists; the file does.
/// </summary>
/// <remarks>
/// A read is two steps -- resolve the header, then open the file it names -- and a writer replacing
/// the payload between them deletes what the header named. The header was accurate when it was read
/// and stale by the time it was used, so nothing is broken and nobody is at fault: this is a
/// time-of-check/time-of-use window, and the honest answer is "that version is gone" rather than
/// "the server failed".
/// <para>
/// Distinct from <see cref="OdinFileHeaderHasCorruptPayloadException"/>, which is the same missing
/// file with the version <i>unchanged</i> -- there the store really has lost data and a 500 is the
/// truth.
/// </para>
/// </remarks>
public class OdinPayloadVersionGoneException : OdinClientException
{
    public OdinPayloadVersionGoneException(string message, Exception innerException)
        : base(message, innerException)
    {
        ErrorCode = OdinClientErrorCode.PayloadVersionGone;
    }
}
