using System;
using Odin.Core.Time;

namespace Odin.Core.Services.AppNotifications.Push;

public class PushNotificationSubscription
{
    public Guid AccessRegistrationId { get; set; }

    public string FriendlyName { get; set; }
    public string Endpoint { get; set; }
    public UnixTimeUtc ExpirationTime { get; set; }
    public string Auth { get; set; }
    public string P256DH { get; set; }

    public UnixTimeUtc SubscriptionStartedDate { get; set; }


    public RedactedPushNotificationSubscription Redacted()
    {
        return new RedactedPushNotificationSubscription()
        {
            FriendlyName = this.FriendlyName,
            AccessRegistrationId = this.AccessRegistrationId,
            SubscriptionStartedDate = this.SubscriptionStartedDate,
            ExpirationTime = this.ExpirationTime,
        };
    }
}

public class RedactedPushNotificationSubscription
{
    public Guid AccessRegistrationId { get; set; }
    
    public string FriendlyName { get; set; }

    public UnixTimeUtc ExpirationTime { get; set; }
    public UnixTimeUtc SubscriptionStartedDate { get; set; }
}