using System;

namespace Odin.Core.Exceptions;

/// <summary>
/// The caller asked for a payload or thumbnail of a file that is deleted: its tombstone keeps none.  Answered as a 404
/// with <see cref="OdinClientErrorCode.FileDeleted"/>, so a client tells it apart from a file that isn't there, which is
/// a 404 with no such code.
/// </summary>
public class OdinFileDeletedException : OdinClientException
{
    public OdinFileDeletedException(string message, Exception innerException = null)
        : base(message, innerException)
    {
        ErrorCode = OdinClientErrorCode.FileDeleted;
    }
}
