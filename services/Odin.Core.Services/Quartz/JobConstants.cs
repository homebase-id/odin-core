namespace Odin.Core.Services.Quartz;

public static class JobConstants
{
    public const string CorrelationIdKey = "odin_correlation_id";

    public const string RetryCountKey = "odin_job_retry_count";
    public const string RetryMaxKey = "odin_job_retry_max";
    public const string RetryDelaySecondsKey = "odin_job_retry_delay_seconds";

    public const string CompletedRetentionSecondsKey = "odin_job_completed_retention_seconds";
    public const string FailedRetentionSecondsKey = "odin_job_failed_retention_seconds";

    public const string StatusKey = "odin_job_status";
    public const string StatusValueAdded = "added";
    public const string StatusValueStarted = "started";
    public const string StatusValueCompleted = "completed";
    public const string StatusValueFailed = "failed";

    public const string ErrorMessageKey = "odin_job_error_message";

    public const string JobToDeleteKey = "odin_job_to_delete";

    public const string UserDefinedDataKey = "odin_job_user_defined_data";

    public const string JobEventTypeKey = "odin_job_event_type";
}
