using System;
using Odin.Services.Drives.DriveCore.Storage;

namespace Odin.Services.Drives.FileSystem.Base;

public class UpdateTransferHistoryData
{
    public LatestTransferStatus? LatestTransferStatus { get; set; }

    public Guid? VersionTag { get; set; }

    public bool? IsInOutbox { get; set; }

    public bool? IsReadByRecipient { get; set; }


    public string ToDebug()
    {
        return $"LatestTransferStatus: {LatestTransferStatus} VersionTag: {VersionTag} IsInOutbox: {IsInOutbox} IsReadByRecipient: {IsReadByRecipient}";
    }
}