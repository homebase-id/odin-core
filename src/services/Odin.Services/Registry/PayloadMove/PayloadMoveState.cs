using System.Collections.Generic;
using System.Text.Json.Serialization;
using Odin.Core.Time;

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
    Refused,

    /// <summary>
    /// Was <see cref="CompleteWithFailures"/>, and the operator gave up the objects the source does not have (#1868):
    /// the next run asks the source for each once more and completes the move without them.
    /// </summary>
    AcceptingMissing
}

/// <summary>
/// The target's checkpoint for one identity's payload move, saved in its job between runs. Counters cover
/// the batches finished so far.
/// </summary>
public class PayloadMoveState
{
    public const int MaxRecordedFailures = 200;

    /// <summary>Beyond this many objects missing at the source, they are counted but not listed, and cannot be accepted.</summary>
    public const int MaxRecordedMissing = 1000;

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

    /// <summary>
    /// The failures that are objects the source does not have (it answered 404), the first
    /// <see cref="MaxRecordedMissing"/> of them; <see cref="MissingCount"/> counts them all. An operator can accept
    /// them (#1868).
    /// </summary>
    public List<PayloadObject> Missing { get; set; } = [];

    public long MissingCount { get; set; }

    /// <summary>When the operator gave up <see cref="Missing"/>: the move completed without them.</summary>
    public UnixTimeUtc? AcceptedMissingAt { get; set; }

    [JsonIgnore]
    public long AcceptedMissing => AcceptedMissingAt == null ? 0 : MissingCount;

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

    /// <summary>Why <see cref="Missing"/> cannot be accepted, or null if it can: the move ended with nothing but those.</summary>
    [JsonIgnore]
    public string? WhyMissingCannotBeAccepted =>
        Status != PayloadMoveStatus.CompleteWithFailures ? $"the transfer is {Status}, not CompleteWithFailures"
        : MissingCount == 0 ? "no failure is recorded as missing at the source; a transfer that finished before " +
                              "accept-missing existed does not record them: run --retry first"
        : FailureCount != MissingCount ? $"{FailureCount - MissingCount} failure(s) are not objects missing at the source"
        : MissingCount != Missing.Count ? $"{MissingCount} objects are missing, more than the {MaxRecordedMissing} " +
                                          "listed; look into the source first"
        : null;

    /// <summary>
    /// Gives up <see cref="Missing"/>: the next run checks them at the source once more and completes without them.
    /// Returns why it cannot, or null once asked.
    /// </summary>
    public string? RequestAcceptMissing()
    {
        if (WhyMissingCannotBeAccepted is { } why)
        {
            return why;
        }

        Status = PayloadMoveStatus.AcceptingMissing;
        return null;
    }

    /// <summary>(Re)starts the walk at the newest file the import brought, with nothing counted yet.</summary>
    public void StartFrom(long startRowId)
    {
        StartRowId = startRowId;
        CursorRowId = startRowId + 1;
        QueuedItemsDone = false;
        Files = Objects = Bytes = Skipped = FailureCount = MissingCount = 0;
        Failures = [];
        Missing = [];
        AcceptedMissingAt = null;
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

    /// <summary>An object the source answered 404 for: a failure, recorded so an operator can accept it.</summary>
    public void AddMissing(PayloadObject payloadObject)
    {
        AddFailure($"{payloadObject}: the source does not have it");
        MissingCount++;
        if (Missing.Count < MaxRecordedMissing)
        {
            Missing.Add(payloadObject);
        }
    }

    /// <summary>A copy without the secrets, for anything shown outside the job.</summary>
    public PayloadMoveState Redacted()
    {
        var copy = (PayloadMoveState)MemberwiseClone();
        copy.HandoffToken = HandoffToken == null ? null : "(redacted)";
        copy.Credential = Credential == null ? null : "(redacted)";
        copy.Failures = [..Failures];
        copy.Missing = [..Missing];
        return copy;
    }
}
