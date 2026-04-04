using System;
using System.Collections.Generic;
using Odin.Core.Time;

namespace Odin.Hosting.UnifiedV2.Drive.Write;

public class SendReadReceiptRequestV2
{
    /// <summary>
    /// The file IDs (within the drive specified by the route) to mark as read.
    /// </summary>
    public List<Guid> Files { get; init; } = [];

    /// <summary>
    /// Optional. The time the files were actually read (milliseconds since Unix epoch).
    /// Useful for offline scenarios where the client read the message earlier than the current time.
    /// If omitted and the file is already marked as read, no update occurs.
    /// If omitted and the file has no read time, the server sets it to now.
    /// If provided, the value is clamped to min(Timestamp, now) to prevent future timestamps,
    /// and only applied when it is later than the file's current read time.
    /// </summary>
    public UnixTimeUtc? Timestamp { get; init; }
}

public class SendReadReceiptByEndTimeRequestV2
{
    /// <summary>Filter: only include files with this file type.</summary>
    public int? FileType { get; init; }

    /// <summary>Filter: only include files with this data type.</summary>
    public int? DataType { get; init; }

    /// <summary>Filter: only include files belonging to this group.</summary>
    public Guid? GroupId { get; init; }

    /// <summary>
    /// Send read receipts for all matching files created on or before this time (milliseconds since Unix epoch).
    /// </summary>
    public UnixTimeUtc EndTime { get; init; }

    /// <summary>
    /// Optional. The time the files were actually read (milliseconds since Unix epoch).
    /// Useful for offline scenarios where the client read the messages earlier than the current time.
    /// If omitted and a file is already marked as read, no update occurs.
    /// If omitted and a file has no read time, the server sets it to now.
    /// If provided, the value is clamped to min(Timestamp, now) to prevent future timestamps,
    /// and only applied when it is later than the file's current read time.
    /// </summary>
    public UnixTimeUtc? Timestamp { get; init; }
}