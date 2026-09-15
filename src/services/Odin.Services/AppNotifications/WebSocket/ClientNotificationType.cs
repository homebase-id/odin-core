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
    /// A connected identity read a drive via the temporal (time-boxed) read API.
    /// </summary>
    TemporalDriveAccessed = 5001,
    /// <summary>
    /// An existing connection's state changed (disconnect/block/unblock) or a circle was granted/revoked to it.
    /// </summary>
    ConnectionChanged = 5002,
    /// <summary>
    /// A circle definition was created, updated, deleted, enabled, or disabled.
    /// </summary>
    CircleDefinitionChanged = 5003,
    /// <summary>
    /// One of the public, static-file artifacts derived from profile attributes (sitedata.json,
    /// public_image.json, public_profile.json) was republished.
    /// </summary>
    PublicProfileContentPublished = 5004,
    /// An opaque live-relay data point (e.g. live GPS) pushed by a connected identity to an app.
    /// Carries the sending identity, a channel key, the opaque blob, and the server-received time.
    /// </summary>
    LiveRelay = 6001,
    /// <summary>
    /// Indicates the notification doesnt need this value.  Note: this implies we might be able to dorp this field all together
    /// </summary>
    Unused = 8001,
    Error = 0xBADBEEF,
    AuthenticationError = 403
    
}