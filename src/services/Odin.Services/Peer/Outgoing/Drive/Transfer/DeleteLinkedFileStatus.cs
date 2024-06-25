namespace Odin.Services.Peer.Outgoing.Drive.Transfer;

public enum DeleteLinkedFileStatus
{
    RequestAccepted,
    RemoteServerFailed,
    
    Enqueued,
    EnqueueFailed
}