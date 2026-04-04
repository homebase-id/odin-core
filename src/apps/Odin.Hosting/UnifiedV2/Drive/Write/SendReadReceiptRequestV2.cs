using System;
using System.Collections.Generic;
using Odin.Core.Time;

namespace Odin.Hosting.UnifiedV2.Drive.Write;

public class SendReadReceiptRequestV2
{
    public List<Guid> Files { get; init; } = [];

    /// <summary>
    /// Optional. The time the files were read. If omitted, the current server time is used.
    /// Clamped to min(Timestamp, now) to prevent future timestamps.
    /// Only updates if later than the existing read time.
    /// </summary>
    public UnixTimeUtc? Timestamp { get; init; }
}

// public class SendReadReceiptByEndTimeRequestV2
// {
//     public int? FileType { get; init; }
//     public int? DataType { get; init; }
//     public Guid? GroupId { get; init; }

//     /// <summary>
//     /// Sends read receipts for all matching files created on or before the specified time.
//     /// </summary>
//     public UnixTimeUtc EndTime { get; init; }

//     /// <summary>
//     /// Optional. The time the files were read. If omitted, the current server time is used.
//     /// Clamped to min(Timestamp, now) to prevent future timestamps.
//     /// Only updates if later than the existing read time.
//     /// </summary>
//     public UnixTimeUtc? Timestamp { get; init; }
// }