using System.Collections.Generic;

namespace Odin.Services.Peer.Outgoing.Drive.Transfer;

public class SendReadReceiptResult
{
    public Dictionary<string, SendReadReceiptResultStatus> Results { get; set; }
}

public enum SendReadReceiptResultStatus
{
    RequestAcceptedIntoInbox = 1,
    RemoteServerFailed = 2
}