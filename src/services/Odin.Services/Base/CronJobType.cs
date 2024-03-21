namespace Odin.Services.Base;

public enum CronJobType
{
    PendingTransitTransfer = 101,
    FeedDistribution = 303,
    PushNotification = 909,
    ReconcileInboxOutbox = 808
}