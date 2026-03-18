using System;
using System.Collections.Generic;
using Odin.Core.Time;

namespace Odin.Hosting.UnifiedV2.Drive.Write;

public class SendReadReceiptRequestV2
{
    public List<Guid> Files { get; init; } = [];
}

public class SendReadReceiptByEndTimeRequestV2
{
    public int? FileType { get; init; }
    public int? DataType { get; init; }
    public Guid? GroupId { get; init; }

    /// <summary>
    /// Sends read receipts for all matching files created on or before the specified time.
    /// </summary>
    public UnixTimeUtc EndTime { get; init; }
}