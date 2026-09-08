using System;
using Odin.Core.Time;

namespace Odin.Services.AppNotifications.Push;

public class PushNotificationSubscription
{
    public Guid AccessRegistrationId { get; set; }

    public string FriendlyName { get; set; }
    public string Endpoint { get; set; }
    public UnixTimeUtc ExpirationTime { get; set; }
    public string Auth { get; set; }
    public string P256DH { get; set; }

    public UnixTimeUtc SubscriptionStartedDate { get; set; }

    public string FirebaseDeviceToken { get; set; }
    public string FirebaseDevicePlatform { get; set; }
    
    public RedactedPushNotificationSubscription Redacted()
    {
        return new RedactedPushNotificationSubscription()
        {
            FriendlyName = this.FriendlyName,
            AccessRegistrationId = this.AccessRegistrationId,
            Endpoint = this.Endpoint,
            SubscriptionStartedDate = this.SubscriptionStartedDate,
            ExpirationTime = this.ExpirationTime,
            FirebaseDeviceToken = this.FirebaseDeviceToken,
        };
    }

}

public class RedactedPushNotificationSubscription
{
    public Guid AccessRegistrationId { get; set; }

    public string FriendlyName { get; set; }

    // A web (VAPID) subscription has no FirebaseDeviceToken, so without this the caller cannot tell a
    // healthy subscription from none at all, nor notice that the browser rotated its push endpoint.
    // Auth/P256DH stay hidden: they are the payload encryption secrets; the endpoint alone is not
    // sendable without a VAPID signature from the identity's private key.
    public string Endpoint { get; set; }

    public UnixTimeUtc ExpirationTime { get; set; }
    public UnixTimeUtc SubscriptionStartedDate { get; set; }

    public string FirebaseDeviceToken { get; set; }
}
