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

    /// <summary>
    /// The device's PushKit VoIP token, iOS only, null for every other device. Carried to the push
    /// relay with each push so it can send a Ring as a VoIP push. Stored as JSON, so rows written
    /// before this field existed read back as null.
    /// </summary>
    public string VoipDeviceToken { get; set; }

    public RedactedPushNotificationSubscription Redacted()
    {
        return new RedactedPushNotificationSubscription()
        {
            FriendlyName = this.FriendlyName,
            AccessRegistrationId = this.AccessRegistrationId,
            SubscriptionStartedDate = this.SubscriptionStartedDate,
            ExpirationTime = this.ExpirationTime,
            FirebaseDeviceToken = this.FirebaseDeviceToken,
            VoipDeviceToken = this.VoipDeviceToken,
        };
    }

}

public class RedactedPushNotificationSubscription
{
    public Guid AccessRegistrationId { get; set; }

    public string FriendlyName { get; set; }

    public UnixTimeUtc ExpirationTime { get; set; }
    public UnixTimeUtc SubscriptionStartedDate { get; set; }

    public string FirebaseDeviceToken { get; set; }

    /// <summary>Echoed so a client can verify its PushKit token is registered.</summary>
    public string VoipDeviceToken { get; set; }
}
