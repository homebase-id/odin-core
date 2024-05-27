namespace Odin.Services.AppNotifications.WebSocket;

public enum ClientNotificationType
{
    /// <summary>
    /// Indicates the device handshake complete and it will receive notifications
    /// </summary>
    DeviceHandshakeSuccess = 1,
    Pong = 2,
    FileAdded = 101,
    FileDeleted = 202,
    FileModified = 303,
    ConnectionRequestReceived = 505,
    DeviceConnected = 606,
    DeviceDisconnected = 707,
    ConnectionRequestAccepted = 808,
    TransitFileReceived = 909,
    NewFollower = 1001,
    StatisticsChanged = 1002,
    ReactionContentAdded = 2003,
    ReactionContentDeleted = 2004,
    AllReactionsByFileDeleted = 2005,
    AppNotificationAdded = 3001,

    OutboxFileItemDeliverySuccess = 5005,
    OutboxFileItemDeliveryFailed = 5008,
    
    Error = 0xBADBEEF,
}