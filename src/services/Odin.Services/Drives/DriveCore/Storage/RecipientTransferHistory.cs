using System;
using System.Collections.Generic;
using Odin.Core.Identity;
using Odin.Core.Time;

namespace Odin.Services.Drives.DriveCore.Storage;

public class RecipientTransferHistory
{
    public TransferHistorySummary Summary { get; init; }
}

public class TransferHistorySummary
{
    public int TotalInOutbox { get; set; }
    public int TotalFailed { get; set; }
    public int TotalDelivered { get; set; }
    public int TotalReadByRecipient { get; set; }
}

public class RecipientTransferHistoryItem
{
    public OdinId Recipient { get; init; }
    
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
    /// No value specified
    /// </summary>
    None = 0,

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