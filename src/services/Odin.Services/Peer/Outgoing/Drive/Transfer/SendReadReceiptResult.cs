using System.Collections.Generic;
using Odin.Core.Identity;
using Odin.Services.Drives;

namespace Odin.Services.Peer.Outgoing.Drive.Transfer;

public class SendReadReceiptResult
{
    public List<SendReadReceiptResultFileItem> Results { get; set; }
}

public class SendReadReceiptResultFileItem
{
    public ExternalFileIdentifier File { get; set; }

    public List<SendReadReceiptResultRecipientStatusItem> Status { get; set; }
}

public class SendReadReceiptResultRecipientStatusItem
{
    public OdinId? Recipient { get; set; }

    public SendReadReceiptResultStatus Status { get; set; }
}

public enum SendReadReceiptResultStatus
{
    NotConnectedToOriginalSender = 6,
    FileDoesNotExist = 8,
    FileDoesNotHaveSender = 9,
    MissingGlobalTransitId = 10,
    Enqueued = 11,
    CannotSendReadReceiptToSelf = 12
}