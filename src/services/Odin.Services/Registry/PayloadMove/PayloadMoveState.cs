using System.Collections.Generic;
using System.Text.Json.Serialization;

#nullable enable

namespace Odin.Services.Registry.PayloadMove;

public enum PayloadMoveStatus
{
    /// <summary>Working through the files.</summary>
    Transferring,

    /// <summary>Waiting on the source at the longest backoff: it is throttling or unreachable.</summary>
    Throttled,

    /// <summary>Every object arrived and the source was told.</summary>
    Complete,

    /// <summary>Every file was visited but some objects could not be fetched; the source is not released.</summary>
    CompleteWithFailures,

    /// <summary>The source refused the handoff token: already redeemed (a second import) or expired.</summary>
    Refused
}

/// <summary>
/// The target's checkpoint for one identity's payload move, saved in its job between runs. Counters cover
/// the batches finished so far.
/// </summary>
public class PayloadMoveState
{
    public const int MaxRecordedFailures = 200;

    public string BaseUrl { get; set; } = "";

    /// <summary>Until the first run redeems it; then null, and <see cref="Credential"/> is set.</summary>
    public string? HandoffToken { get; set; }

    public string? Credential { get; set; }

    /// <summary>The highest file rowId the import created. Files above it were made here and are skipped.</summary>
    public long StartRowId { get; set; }

    /// <summary>The next files to transfer are those below this rowId, newest first.</summary>
    public long CursorRowId { get; set; }

    /// <summary>
    /// The payloads of the carried Inbox and Outbox items have been fetched, before the walk starts (#1871). Some
    /// may have failed; those are among <see cref="Failures"/>.
    /// </summary>
    public bool QueuedItemsDone { get; set; }

    public long Files { get; set; }
    public long Objects { get; set; }
    public long Bytes { get; set; }
    public long Skipped { get; set; }

    /// <summary>The first <see cref="MaxRecordedFailures"/> failures; <see cref="FailureCount"/> counts them all.</summary>
    public List<string> Failures { get; set; } = [];

    public long FailureCount { get; set; }

    public int BackoffSeconds { get; set; }

    public PayloadMoveStatus Status { get; set; }

    [JsonIgnore]
    public bool IsFinished => Status is PayloadMoveStatus.Complete or PayloadMoveStatus.CompleteWithFailures or PayloadMoveStatus.Refused;

    /// <summary>
    /// Resuming the identity waits: its owner's app could process a queued Inbox item, or its Outbox send one,
    /// before the item's payloads are here. Processing an Inbox item without them loses it.
    /// </summary>
    [JsonIgnore]
    public bool HoldsResume => !IsFinished && !QueuedItemsDone;

    /// <summary>(Re)starts the walk at the newest file the import brought, with nothing counted yet.</summary>
    public void StartFrom(long startRowId)
    {
        StartRowId = startRowId;
        CursorRowId = startRowId + 1;
        QueuedItemsDone = false;
        Files = Objects = Bytes = Skipped = FailureCount = 0;
        Failures = [];
        BackoffSeconds = 0;
        Status = PayloadMoveStatus.Transferring;
    }

    public void AddFailure(string failure)
    {
        FailureCount++;
        if (Failures.Count < MaxRecordedFailures)
        {
            Failures.Add(failure);
        }
    }

    /// <summary>A copy without the secrets, for anything shown outside the job.</summary>
    public PayloadMoveState Redacted()
    {
        var copy = (PayloadMoveState)MemberwiseClone();
        copy.HandoffToken = HandoffToken == null ? null : "(redacted)";
        copy.Credential = Credential == null ? null : "(redacted)";
        copy.Failures = [..Failures];
        return copy;
    }
}
