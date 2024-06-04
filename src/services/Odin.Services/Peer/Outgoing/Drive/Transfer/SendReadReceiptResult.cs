namespace Odin.Services.Peer.Outgoing.Drive.Transfer;

public class SendReadReceiptResult
{
    public SendReadReceiptResultStatus Status { get; set; }
}

public enum SendReadReceiptResultStatus
{
    RequestAccepted = 1,
    RemoteServerFailed = 2
}