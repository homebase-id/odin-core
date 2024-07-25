namespace Odin.Services.Peer.Outgoing.Drive.Transfer.Outbox;

public enum OutboxItemType
{
    File = 100,

    PushNotification = 300,

    UnencryptedFeedItem = 500,

    ReadReceipt = 900,

    DeleteRemoteFile = 700,
    
    UpdateRemotePayloads = 1100,
    
    DeleteRemotePayloads = 1200
}