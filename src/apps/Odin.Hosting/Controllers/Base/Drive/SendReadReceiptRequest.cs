using System.Collections.Generic;
using Odin.Core.Time;
using Odin.Services.Drives;

namespace Odin.Hosting.Controllers.Base.Drive;

public class SendReadReceiptRequest
{
    public List<ExternalFileIdentifier> Files { get; set; }

    /// <summary>
    /// Optional. The time the files were actually read (milliseconds since Unix epoch).
    /// Useful for offline scenarios where the client read the message earlier than the current time.
    /// If omitted and the file is already marked as read, no update occurs.
    /// If omitted and the file has no read time, the server sets it to now.
    /// If provided, the value is clamped to min(Timestamp, now) to prevent future timestamps,
    /// and only applied when it is later than the file's current read time.
    /// </summary>
    public UnixTimeUtc? Timestamp { get; set; }
}