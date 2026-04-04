using System.Collections.Generic;
using Odin.Core.Time;
using Odin.Services.Drives;

namespace Odin.Hosting.Controllers.Base.Drive;

public class SendReadReceiptRequest
{
    public List<ExternalFileIdentifier> Files { get; set; }

    /// <summary>
    /// Optional. The time the files were read. Clamped to min(Timestamp, now).
    /// Only updates if later than the existing read time.
    /// </summary>
    public UnixTimeUtc? Timestamp { get; set; }
}