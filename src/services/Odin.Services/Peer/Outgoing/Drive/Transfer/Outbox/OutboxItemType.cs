namespace Odin.Services.Peer.Outgoing.Drive.Transfer.Outbox;

public enum OutboxItemType
{
    File = 100,

    PushNotification = 300,

    UnencryptedFeedItem = 500,

    DeleteRemoteFile = 700,
    
    ReadReceipt = 900
}