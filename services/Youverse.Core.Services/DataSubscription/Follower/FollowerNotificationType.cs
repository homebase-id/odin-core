namespace Youverse.Core.Services.DataSubscription.Follower;

public enum FollowerNotificationType
{
    /// <summary>
    /// Send notifications for all content with-in the subscriber's access
    /// </summary>
    AllNotifications = 1,
        
    /// <summary>
    /// Send notifications for the specified channels
    /// </summary>
    SelectedChannels = 2
}