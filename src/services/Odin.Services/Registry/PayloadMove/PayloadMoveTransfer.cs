using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core.Serialization;
using Odin.Core.Storage.Database.Identity.Table;
using Odin.Services.Drives.DriveCore.Storage;

#nullable enable

namespace Odin.Services.Registry.PayloadMove;

public enum SliceEnd
{
    /// <summary>More files to go: run again straight away.</summary>
    Continue,

    /// <summary>The source is throttling or unreachable: run again after <see cref="SliceResult.Wait"/>.</summary>
    Wait,

    /// <summary>Every file visited (or the handoff refused); see the state's status.</summary>
    Finished
}

public readonly record struct SliceResult(SliceEnd End, TimeSpan Wait = default);

/// <summary>
/// The target side of a payload move, one slice at a time: newest files first, from the cursor down to the
/// oldest file the import brought, fetching each file's payloads and thumbnails from the source, at most
/// <c>parallelism</c> at once, into the target store. Everything it needs to resume is in the
/// <see cref="PayloadMoveState"/> it is given. See docs/superpowers/specs/2026-08-31-payload-migration-design.md.
/// </summary>
public sealed class PayloadMoveTransfer(
    IPayloadMoveSourceClient source,
    IDriveFileStore target,
    Func<long, int, Task<List<FilePayloadRow>>> filesBelow,
    Func<PayloadObject, string> targetPathOf,
    int parallelism,
    ILogger logger)
{
    public const int FilesPerBatch = 50;
    public static readonly TimeSpan FirstBackoff = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan MaxBackoff = TimeSpan.FromMinutes(10);
    private const int AttemptsPerObject = 3;

    private enum Result { Transferred, Skipped, Failed, Stalled }

    private readonly record struct ObjectOutcome(Result Result, long Bytes = 0, string? Failure = null, TimeSpan? RetryAfter = null);

    public async Task<SliceResult> RunSliceAsync(PayloadMoveState state, TimeSpan budget, CancellationToken cancellationToken)
    {
        if (state.IsFinished)
        {
            return new SliceResult(SliceEnd.Finished);
        }

        if (state.Credential == null && !await RedeemAsync(state, cancellationToken))
        {
            return state.IsFinished ? new SliceResult(SliceEnd.Finished) : Backoff(state, null);
        }

        var until = DateTimeOffset.UtcNow + budget;
        while (DateTimeOffset.UtcNow < until)
        {
            var files = await filesBelow(state.CursorRowId, FilesPerBatch);
            if (files.Count == 0)
            {
                return await FinishAsync(state, cancellationToken);
            }

            var unreadable = new List<string>();
            var objects = files.SelectMany(file => ObjectsOf(file, unreadable)).ToList();
            var outcomes = await TransferAsync(objects, state.Credential!, cancellationToken);

            // A stalled object means the source is throttling or unreachable: leave the cursor where it is and
            // wait. What did land is skipped next time, so nothing is fetched twice.
            if (outcomes.FirstOrDefault(o => o.outcome.Result == Result.Stalled) is { outcome.Result: Result.Stalled } stalled)
            {
                return Backoff(state, stalled.outcome.RetryAfter);
            }

            unreadable.ForEach(state.AddFailure);
            foreach (var (payloadObject, outcome) in outcomes)
            {
                switch (outcome.Result)
                {
                    case Result.Transferred:
                        state.Objects++;
                        state.Bytes += outcome.Bytes;
                        break;
                    case Result.Skipped:
                        state.Skipped++;
                        break;
                    case Result.Failed:
                        state.AddFailure($"{payloadObject}: {outcome.Failure}");
                        logger.LogWarning("Payload move could not transfer {object}: {failure}", payloadObject, outcome.Failure);
                        break;
                }
            }

            state.Files += files.Count;
            state.CursorRowId = files.Min(file => file.RowId);
            state.BackoffSeconds = 0;
            state.Status = PayloadMoveStatus.Transferring;
        }

        return new SliceResult(SliceEnd.Continue);
    }

    //

    private async Task<bool> RedeemAsync(PayloadMoveState state, CancellationToken cancellationToken)
    {
        var (outcome, credential) = await source.RedeemAsync(state.HandoffToken ?? "", cancellationToken);
        switch (outcome.Result)
        {
            case FetchResult.Fetched:
                state.Credential = credential;
                state.HandoffToken = null;
                return true;
            case FetchResult.NotFound:
                // Loud: another import already holds this identity's payloads, or the file sat too long
                state.Status = PayloadMoveStatus.Refused;
                state.AddFailure("The source refused the handoff token: it was already redeemed (a second import of the " +
                                 "same export file) or it expired. Export again to move the payloads.");
                logger.LogError("Payload move refused by the source {baseUrl}: handoff token already redeemed or expired", state.BaseUrl);
                return false;
            default:
                logger.LogWarning("Payload move could not reach the source {baseUrl} to start: {error}", state.BaseUrl, outcome.Error);
                return false;
        }
    }

    private async Task<SliceResult> FinishAsync(PayloadMoveState state, CancellationToken cancellationToken)
    {
        if (state.FailureCount > 0)
        {
            // The source keeps the identity's payloads, and cannot be deleted, until someone looks at these
            state.Status = PayloadMoveStatus.CompleteWithFailures;
            logger.LogError("Payload move finished with {count} object(s) it could not transfer; the source is not released",
                state.FailureCount);
            return new SliceResult(SliceEnd.Finished);
        }

        var outcome = await source.CompleteAsync(state.Credential!, cancellationToken);
        if (outcome.Result != FetchResult.Fetched)
        {
            // Everything is here; only the source has not heard. Try again later.
            logger.LogWarning("Payload move could not tell the source it is complete: {result} {error}", outcome.Result, outcome.Error);
            return Backoff(state, outcome.RetryAfter);
        }

        state.Status = PayloadMoveStatus.Complete;
        state.BackoffSeconds = 0;
        logger.LogInformation("Payload move complete: {objects} object(s), {bytes} bytes, {skipped} already here",
            state.Objects, state.Bytes, state.Skipped);
        return new SliceResult(SliceEnd.Finished);
    }

    private static SliceResult Backoff(PayloadMoveState state, TimeSpan? retryAfter)
    {
        var wait = retryAfter ?? (state.BackoffSeconds == 0 ? FirstBackoff : TimeSpan.FromSeconds(state.BackoffSeconds * 2));
        if (wait > MaxBackoff)
        {
            wait = MaxBackoff;
        }

        state.BackoffSeconds = (int)wait.TotalSeconds;
        state.Status = wait >= MaxBackoff ? PayloadMoveStatus.Throttled : PayloadMoveStatus.Transferring;
        return new SliceResult(SliceEnd.Wait, wait);
    }

    private async Task<List<(PayloadObject payloadObject, ObjectOutcome outcome)>> TransferAsync(
        List<PayloadObject> objects, string credential, CancellationToken cancellationToken)
    {
        using var gate = new SemaphoreSlim(Math.Max(1, parallelism));
        var transfers = objects.Select(async payloadObject =>
        {
            await gate.WaitAsync(cancellationToken);
            try
            {
                return (payloadObject, await TransferOneAsync(payloadObject, credential, cancellationToken));
            }
            finally
            {
                gate.Release();
            }
        });

        return (await Task.WhenAll(transfers)).ToList();
    }

    private async Task<ObjectOutcome> TransferOneAsync(PayloadObject payloadObject, string credential, CancellationToken cancellationToken)
    {
        var path = targetPathOf(payloadObject);
        if (await target.ExistsAsync(path, cancellationToken) &&
            (payloadObject.ExpectedLength <= 0 || await target.LengthAsync(path, cancellationToken) == payloadObject.ExpectedLength))
        {
            return new ObjectOutcome(Result.Skipped);
        }

        for (var attempt = 1; ; attempt++)
        {
            // Through a local file: the target store needs a stream of known length, and a size check comes
            // before anything is written there
            var temp = Path.Combine(Path.GetTempPath(), $"odin-payload-move-{Guid.NewGuid():N}");
            await using var file = new FileStream(temp, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 81920,
                FileOptions.DeleteOnClose | FileOptions.Asynchronous);

            var fetched = await source.FetchAsync(payloadObject, credential, file, cancellationToken);
            switch (fetched.Result)
            {
                case FetchResult.Fetched:
                    if (payloadObject.ExpectedLength > 0 && file.Length != payloadObject.ExpectedLength)
                    {
                        return new ObjectOutcome(Result.Failed,
                            Failure: $"the source sent {file.Length} bytes, the header records {payloadObject.ExpectedLength}");
                    }

                    file.Position = 0;
                    await target.EnsureDirectoryAsync(Path.GetDirectoryName(path)!, cancellationToken);
                    await target.WriteStreamAsync(path, file, cancellationToken);
                    return new ObjectOutcome(Result.Transferred, file.Length);

                case FetchResult.NotFound:
                    return new ObjectOutcome(Result.Failed, Failure: "the source does not have it");

                case FetchResult.Throttled:
                    return new ObjectOutcome(Result.Stalled, RetryAfter: fetched.RetryAfter);

                default:
                    if (attempt >= AttemptsPerObject)
                    {
                        logger.LogWarning("Payload move could not fetch {object} after {attempts} attempts: {error}",
                            payloadObject, attempt, fetched.Error);
                        return new ObjectOutcome(Result.Stalled, RetryAfter: fetched.RetryAfter);
                    }

                    await Task.Delay(TimeSpan.FromSeconds(attempt), cancellationToken);
                    break;
            }
        }
    }

    // A file's stored objects: each payload and its thumbnails. Files whose payloads live elsewhere, and
    // deleted files, have none.
    private static IEnumerable<PayloadObject> ObjectsOf(FilePayloadRow file, List<string> unreadable)
    {
        FileMetadata? metadata = null;
        try
        {
            metadata = file.FileMetaData == null ? null : OdinSystemSerializer.Deserialize<FileMetadata>(file.FileMetaData);
        }
        catch (Exception e)
        {
            unreadable.Add($"file {file.FileId}: its header could not be read ({e.Message})");
        }

        if (metadata == null || metadata.PayloadsAreRemote || metadata.Payloads == null)
        {
            yield break;
        }

        foreach (var payload in metadata.Payloads)
        {
            yield return new PayloadObject(file.DriveId, file.FileId, payload.Key, payload.Uid, payload.BytesWritten);
            foreach (var thumbnail in payload.Thumbnails ?? [])
            {
                yield return new PayloadObject(file.DriveId, file.FileId, payload.Key, payload.Uid, thumbnail.BytesWritten,
                    thumbnail.PixelWidth, thumbnail.PixelHeight);
            }
        }
    }
}
