namespace Youverse.Core.Services.Base;

public enum CronJobType
{
    PendingTransfer = 101,
    GenerateCertificate = 202,
    FeedReactionPreviewDistribution = 303,
    FeedFileDistribution = 350
}