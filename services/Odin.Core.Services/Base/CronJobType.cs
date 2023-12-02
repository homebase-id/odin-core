namespace Odin.Core.Services.Base;

public enum CronJobType
{
    PendingTransitTransfer = 101,
    GenerateCertificate = 202,
    FeedDistribution = 303,
    PushNotification = 909
}