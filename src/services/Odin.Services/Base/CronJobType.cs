namespace Odin.Services.Base;

public enum CronJobType
{
    PendingTransitTransfer = 101,
    FeedDistribution = 303,
    ReconcileInboxOutbox = 808
}