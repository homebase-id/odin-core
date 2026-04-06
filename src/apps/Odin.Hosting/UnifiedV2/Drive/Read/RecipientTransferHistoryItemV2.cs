using System;
using Odin.Core.Identity;
using Odin.Core.Time;
using Odin.Services.Drives.DriveCore.Storage;

namespace Odin.Hosting.UnifiedV2.Drive.Read;

public class RecipientTransferHistoryItemV2
{
    public OdinId Recipient { get; init; }
    public UnixTimeUtc LastUpdated { get; set; }
    public LatestTransferStatus LatestTransferStatus { get; set; }
    public bool IsInOutbox { get; set; }
    public Guid? LatestSuccessfullyDeliveredVersionTag { get; set; }

    /// <summary>
    /// Timestamp (Unix epoch milliseconds) when the recipient read the file, or null if not read.
    /// </summary>
    public long? IsReadByRecipient { get; set; }
}
