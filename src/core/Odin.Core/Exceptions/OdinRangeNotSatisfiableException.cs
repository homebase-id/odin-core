using System;

namespace Odin.Core.Exceptions;

/// <summary>
/// A byte range that starts at or past the end of the payload (RFC 9110 14.1.2): an HTTP 416, whose
/// <c>Content-Range: bytes */size</c> tells the client the size.
/// </summary>
public class OdinRangeNotSatisfiableException(Int64 size, string message) : OdinClientException(message)
{
    public Int64 Size { get; } = size;
}
