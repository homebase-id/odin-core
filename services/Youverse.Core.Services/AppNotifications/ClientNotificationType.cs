namespace Youverse.Core.Services.AppNotifications;

public enum ClientNotificationType
{
    /// <summary>
    /// Indicates the device handshake complete and it will receive notifications
    /// </summary>
    DeviceHandshakeSuccess = 1,
    FileAdded = 101,
    FileDeleted = 202,
    FileModified = 303,
    ConnectionRequestReceived = 505,
    DeviceConnected = 606,
    DeviceDisconnected = 707,
    ConnectionRequestAccepted = 808,
    TransitFileReceived = 909,
    NewFollower = 1001,
    StatisticsChanged = 1002
}