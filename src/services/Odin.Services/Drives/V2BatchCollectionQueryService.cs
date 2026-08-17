using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core.Exceptions;
using Odin.Core.Storage;
using Odin.Services.Base;
using Odin.Services.Drives.Management;
using Odin.Services.Peer.Incoming.Drive.Transfer;
using Odin.Services.Util;

namespace Odin.Services.Drives;

/// <summary>
/// Runs a V2 query-batch-collection.
///
/// Two properties distinguish this from the V1 <see cref="FileSystem.Base.DriveQueryServiceBase.GetBatchCollection"/>,
/// which is deliberately left untouched because V1 has other consumers:
///
/// 1. <b>Per-section fault isolation.</b> A section-level fault — unknown drive, archived drive, no read grant,
///    anything unexpected — produces a failed section, never a failed collection.  Only a malformed request or
///    an unroutable response (duplicate section names) is a whole-call 400.
/// 2. <b>A request-level record budget.</b> <see cref="QueryBatchCollectionRequestV2.MaxRecords"/> is a total for
///    the call, filled greedily in request order, so the response is bounded no matter how many sections were
///    submitted.  Section order is caller-controlled and is never reordered: a client that wants fairness across
///    its drives rotates the section order itself.
///
/// Sections are executed through the same <c>fs.Query.GetBatch</c> the single V2 query-batch endpoint uses, so the
/// two agree on drive validity and read permission — including anonymous-readable drives — by construction.
/// </summary>
public class V2BatchCollectionQueryService(
    FileSystemResolver fileSystemResolver,
    IDriveManager driveManager,
    InboxDrainOnQuery inboxDrainOnQuery,
    ILogger<V2BatchCollectionQueryService> logger)
{
    /// <summary>
    /// Upper bound on <see cref="QueryBatchCollectionRequestV2.MaxRecords"/>.  A larger request is clamped to this
    /// rather than rejected, so the caller just pages through via <c>hasMoreRows</c>.
    ///
    /// This is a safety cap, not an expected value: a normal caller asks for a few hundred, and the cap only exists
    /// so one request cannot ask for an unbounded response.  At a rough 10KB per header this is ~10MB, which is
    /// already at the limit of what is sensible for a single response on the high-latency links this endpoint
    /// exists to serve.  Treat 10KB as optimistic — headers carry preview thumbnails unless the caller excludes
    /// them, and transfer history grows with recipient count.
    /// </summary>
    public const int MaxRecordCeiling = 1000;

    /// <summary>
    /// Clamps a requested record budget to <see cref="MaxRecordCeiling"/>.  Callers must reject values below 1
    /// before calling this; it does not validate.
    /// </summary>
    public static int ClampRecordBudget(int requested) => Math.Min(requested, MaxRecordCeiling);

    /// <param name="request">the collection to run</param>
    /// <param name="defaultFileSystemType">
    /// request-level file system (<c>?xfst=</c> / <c>X-ODIN-FILE-SYSTEM-TYPE</c>), used for any section that does
    /// not specify its own
    /// </param>
    /// <param name="odinContext">caller context</param>
    public async Task<QueryBatchCollectionResponseV2> GetBatchCollectionAsync(
        QueryBatchCollectionRequestV2 request,
        FileSystemType defaultFileSystemType,
        IOdinContext odinContext)
    {
        OdinValidationUtils.AssertNotNull(request, nameof(request));
        OdinValidationUtils.AssertNotNull(request.Queries, nameof(request.Queries));

        if (request.MaxRecords < 1)
        {
            throw new OdinClientException("maxRecords must be at least 1", OdinClientErrorCode.InvalidQuery);
        }

        foreach (var section in request.Queries)
        {
            section.AssertIsValid();
        }

        // The name is how the caller matches a result back to the query it asked, so duplicates make the
        // response unroutable.  This one genuinely has to fail the whole call.
        if (request.Queries.DistinctBy(q => q.Name).Count() != request.Queries.Count)
        {
            throw new OdinClientException("The Names of Queries must be unique", OdinClientErrorCode.InvalidQuery);
        }

        var collection = new QueryBatchCollectionResponseV2();
        var remaining = ClampRecordBudget(request.MaxRecords);

        foreach (var section in request.Queries)
        {
            var options = section.ResultOptionsRequest ?? new QueryBatchCollectionSectionOptionsV2();

            if (remaining < 1)
            {
                collection.Results.Add(QueryBatchCollectionSectionV2.BudgetExhausted(section.Name, options.CursorState));
                continue;
            }

            var result = await RunSectionAsync(section, options, remaining, defaultFileSystemType, odinContext);
            collection.Results.Add(result);

            // A failed section consumes no budget and does not stop the fill.
            if (result.Status == QueryBatchSectionStatus.Ok)
            {
                remaining -= result.SearchResults.Count();
            }
        }

        return collection;
    }

    private async Task<QueryBatchCollectionSectionV2> RunSectionAsync(
        CollectionQueryParamSectionV2 section,
        QueryBatchCollectionSectionOptionsV2 options,
        int budget,
        FileSystemType defaultFileSystemType,
        IOdinContext odinContext)
    {
        try
        {
            var fs = fileSystemResolver.ResolveFileSystem(section.FileSystemType ?? defaultFileSystemType);

            // Checked here rather than relying on GetBatch's own asserts, because those collapse "no such drive"
            // and "archived" into a single error code and we owe the caller the distinction.  GetBatch re-asserts
            // both anyway; the order matches it, so this exposes nothing a single query-batch does not already.
            var drive = await driveManager.GetDriveAsync(section.DriveId);
            if (drive == null)
            {
                return Fail(section, QueryBatchSectionStatus.DriveNotFound,
                    $"Invalid drive id {section.DriveId}", OdinClientErrorCode.InvalidDrive);
            }

            if (drive.IsArchived && !odinContext.Caller.HasMasterKey)
            {
                return Fail(section, QueryBatchSectionStatus.DriveArchived,
                    "Drive is archived", OdinClientErrorCode.InvalidDrive);
            }

            // After the budget check, so sections we skip this round do not pay for a drain.  This swallows its
            // own exceptions, so it cannot fail the section.
            await inboxDrainOnQuery.DrainIfReadyAsync(section.DriveId, odinContext);

            var batch = await fs.Query.GetBatch(
                section.DriveId,
                section.QueryParams,
                options.ToQueryBatchResultOptions(maxRecords: budget),
                odinContext);

            return QueryBatchCollectionSectionV2.FromResult(section.Name, batch);
        }
        catch (OdinSecurityException e)
        {
            return Fail(section, QueryBatchSectionStatus.NoReadGrant, e.Message, NullIfNone(e.ErrorCode), e);
        }
        catch (OdinClientException e)
        {
            return Fail(section, QueryBatchSectionStatus.Error, e.Message, NullIfNone(e.ErrorCode), e);
        }
        catch (Exception e)
        {
            // Unexpected, so this one is worth an error-level line — the client-driven faults above are not.
            // Deliberately generic on the wire; the detail stays in the log, correlated by the Serilog
            // CorrelationId enricher.
            logger.LogError(e,
                "query-batch-collection section failed unexpectedly. Section: {section}, DriveId: {driveId}",
                section.Name, section.DriveId);

            return QueryBatchCollectionSectionV2.Failed(
                section.Name, QueryBatchSectionStatus.Error, "The query failed");
        }
    }

    private static OdinClientErrorCode? NullIfNone(OdinClientErrorCode code) =>
        code == OdinClientErrorCode.NoErrorCode ? null : code;

    private QueryBatchCollectionSectionV2 Fail(
        CollectionQueryParamSectionV2 section,
        QueryBatchSectionStatus status,
        string message,
        OdinClientErrorCode? errorCode,
        Exception exception = null)
    {
        logger.LogDebug(exception,
            "query-batch-collection section failed. Section: {section}, DriveId: {driveId}, Status: {status}, Message: {message}",
            section.Name, section.DriveId, status, message);

        return QueryBatchCollectionSectionV2.Failed(section.Name, status, message, errorCode);
    }
}
