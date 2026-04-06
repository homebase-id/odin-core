using System;
using Odin.Services.Drives.DriveCore.Storage;

namespace Odin.Services.Drives.FileSystem.Base;

public class UpdateTransferHistoryData
{
    public LatestTransferStatus? LatestTransferStatus { get; set; }

    public Guid? VersionTag { get; set; }

    public bool? IsInOutbox { get; set; }

    /// <summary>
    /// 0 = not read, positive value = read-at timestamp in milliseconds (UnixTimeUtc)
    /// </summary>
    public Int64? ReadByRecipientTimestamp { get; set; }


    public string ToDebug()
    {
        return $"LatestTransferStatus: {LatestTransferStatus} VersionTag: {VersionTag} IsInOutbox: {IsInOutbox} ReadByRecipientTimestamp: {ReadByRecipientTimestamp}";
    }
}