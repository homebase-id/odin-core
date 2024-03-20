namespace Odin.Services.Peer.Outgoing.Drive.Reactions;

public enum AddDeleteRemoteReactionStatusCode
{
    Failure = 0,
    Success = 1,
    Enqueued = 2,
    RemoteServerAccessDenied = 3
}