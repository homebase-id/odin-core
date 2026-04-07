namespace Odin.Services.AppNotifications.Push;

#nullable enable

public class PushNotificationVerificationResult
{
    public bool HasSubscription { get; set; }
    public string? SubscriptionType { get; set; }
    public bool? IsTokenValid { get; set; }
    public string? InvalidReason { get; set; }
    public RedactedPushNotificationSubscription? Subscription { get; set; }
}
