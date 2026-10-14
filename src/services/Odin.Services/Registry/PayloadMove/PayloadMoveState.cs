using System;
using System.Collections.Generic;
using System.Linq;
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
    // The state is saved in its job's row, which holds at most 65,535 characters; these bound it well below that
    // (PayloadMoveStateTests.AFullStateFitsItsJobRow)
    public const int MaxRecordedFailures = 50;
    public const int MaxFailureLength = 200;

    /// <summary>
    /// More objects missing at the source than this, and the transfer stops: something is wrong with the source
    /// (#1919). Up to it, each is listed, so an operator can accept them.
    /// </summary>
    public const int MaxRecordedMissing = 200;

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
    /// The failures that are objects the source does not have, each once. <see cref="FailureCount"/> counts them
    /// too, and <see cref="Failures"/> does not list them. An operator can accept them (#1868).
    /// </summary>
    public List<PayloadObject> Missing { get; set; } = [];

    /// <summary>Above <see cref="MaxRecordedMissing"/> only when the transfer stopped over them; then only those are listed.</summary>
    public long MissingCount { get; set; }

    /// <summary>How many of <see cref="Missing"/> the source has confirmed it still lacks, while accepting them.</summary>
    public int AcceptCheckedCount { get; set; }

    /// <summary>Why the last acceptance stopped, until a retry: an object is at the source now, or the source refused.</summary>
    public string? AcceptRefusal { get; set; }

    /// <summary>When the source was told the move completed without <see cref="Missing"/>.</summary>
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

    /// <summary>More objects are missing at the source than are listed: the transfer stopped over them.</summary>
    [JsonIgnore]
    public bool TooManyMissing => MissingCount > MaxRecordedMissing;

    /// <summary>Why <see cref="Missing"/> cannot be accepted, or null if it can: the move ended with nothing but those.</summary>
    [JsonIgnore]
    public string? WhyMissingCannotBeAccepted =>
        Status != PayloadMoveStatus.CompleteWithFailures ? $"the transfer is {Status}, not CompleteWithFailures"
        : AcceptRefusal ?? (
            TooManyMissing ? $"more than {MaxRecordedMissing} objects are missing at the source, and the transfer " +
                             "stopped: look into the source first"
            // Before #1868 a missing object was a failure like any other, with this text
            : MissingCount == 0 && Failures.Any(f => f.EndsWith(MissingText, StringComparison.Ordinal)) ? "the transfer finished before " +
                "accept-missing existed and does not record which failures are missing objects: run --retry first"
            : FailureCount != MissingCount ? $"{FailureCount - MissingCount} failure(s) are not objects missing at the source"
            : null);

    /// <summary>
    /// Gives up <see cref="Missing"/>: the next run checks them at the source once more and completes without them.
    /// The operator says how many they listed, so nothing more goes than they saw. Returns why it cannot, or null
    /// once asked.
    /// </summary>
    public string? RequestAcceptMissing(long listedCount)
    {
        if (WhyMissingCannotBeAccepted is { } why)
        {
            return why;
        }

        if (listedCount != MissingCount)
        {
            return $"{MissingCount} object(s) are missing at the source, not {listedCount}: list them with " +
                   "--list-missing and check them first";
        }

        Status = PayloadMoveStatus.AcceptingMissing;
        BackoffSeconds = 0;
        return null;
    }

    // How a failure read before #1868 when the source did not have the object
    private const string MissingText = ": the source does not have it";

    /// <summary>Starts the walk over, or says why not: a complete move's source revoked the credential when told.</summary>
    public string? RequestRetry()
    {
        if (Status == PayloadMoveStatus.Complete)
        {
            // A retry could only fail at the source, and would lose the record of what was given up
            return "the move is complete and its source released; there is nothing to retry";
        }

        StartFrom(StartRowId);
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
        AcceptCheckedCount = 0;
        AcceptRefusal = null;
        AcceptedMissingAt = null;
        BackoffSeconds = 0;
        Status = PayloadMoveStatus.Transferring;
    }

    public void AddFailure(string failure)
    {
        FailureCount++;
        if (Failures.Count < MaxRecordedFailures)
        {
            Failures.Add(failure.Length > MaxFailureLength ? failure[..(MaxFailureLength - 1)] + "…" : failure);
        }
    }

    /// <summary>
    /// An object the source does not have: a failure, listed so an operator can accept it. The walk can meet an
    /// object twice (an Outbox item's file is fetched first, then again in its place), so it counts once.
    /// </summary>
    public void AddMissing(PayloadObject payloadObject)
    {
        if (Missing.Contains(payloadObject))
        {
            return;
        }

        FailureCount++;
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
