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
    InboxItemReceived = 909,
    NewFollower = 1001,
    StatisticsChanged = 1002,
    ReactionContentAdded = 2003,
    ReactionContentDeleted = 2004,
    AllReactionsByFileDeleted = 2005,
    AppNotificationAdded = 3001,
    IntroductionsReceived = 4001,
    IntroductionAccepted = 4002,
    ConnectionFinalized = 4003,
    /// <summary>
    /// Indicates the notification doesnt need this value.  Note: this implies we might be able to dorp this field all together
    /// </summary>
    Unused = 8001,
    Error = 0xBADBEEF,
    AuthenticationError = 403
    
}