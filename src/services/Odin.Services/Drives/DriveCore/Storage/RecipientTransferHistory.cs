using System;
using System.Collections.Generic;
using Odin.Core.Time;

namespace Odin.Services.Drives.DriveCore.Storage;

public class RecipientTransferHistory
{
    public Dictionary<string, RecipientTransferHistoryItem> Recipients { get; set; } =
        new(StringComparer.InvariantCultureIgnoreCase);
}

public class RecipientTransferHistoryItem
{
    public UnixTimeUtc LastUpdated { get; set; }

    /// <summary>
    /// Indicates the latest known status of a transfer as of the LastUpdated timestmp.  If null
    /// </summary>
    public LatestTransferStatus LatestTransferStatus { get; set; }

    /// <summary>
    /// Indicates if the item is still in the outbox and attempting to be sent
    /// </summary>
    public bool IsInOutbox { get; set; }

    /// <summary>
    /// If set, indicates the last version tag of this file that was sent to this recipient
    /// </summary>
    public Guid? LatestSuccessfullyDeliveredVersionTag { get; set; }

    /// <summary>
    /// Indicates the recipient replied that the file was read (as called by the app)
    /// </summary>
    public bool IsReadByRecipient { get; set; }
}

public enum LatestTransferStatus
{
    /// <summary>
    /// Item was delivered to the recipient server
    /// </summary>
    Delivered = 10,

    /// <summary>
    /// Caller does not have access to the recipient server
    /// </summary>
    RecipientIdentityReturnedAccessDenied = 40,

    /// <summary>
    /// The local file cannot be sent due to it's settings or recipient's permissions
    /// </summary>
    SourceFileDoesNotAllowDistribution = 50,
    
    /// <summary>
    /// The item reached the max number of attempts to send it
    /// </summary>
    SendingServerTooManyAttempts = 55,

    /// <summary>
    /// The recipient server did not respond
    /// </summary>
    RecipientServerNotResponding = 70,

    /// <summary>
    /// Indicates the recipient server returned an http status 500
    /// </summary>
    RecipientIdentityReturnedServerError = 80,

    /// <summary>
    /// Indicates the recipient server detected a bad request from the sending server
    /// </summary>
    RecipientIdentityReturnedBadRequest = 90,

    /// <summary>
    /// Something bad happened on the server
    /// </summary>
    UnknownServerError = 9999,
    
}

/// <summary>
/// used to update a <see cref="RecipientTransferHistoryItem"/> record.  Null values will leave the field unchanged.
/// </summary>
public class UpdateTransferHistoryData
{
    public LatestTransferStatus? LatestTransferStatus { get; set; }

    public Guid? VersionTag { get; set; }
    
    public bool? IsInOutbox { get; set; }

    public bool? IsReadByRecipient { get; set; }
    
}