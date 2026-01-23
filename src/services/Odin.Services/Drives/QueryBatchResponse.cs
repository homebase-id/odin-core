using System;
using System.Collections.Generic;
using Odin.Core.Time;
using Odin.Services.Apps;

namespace Odin.Services.Drives;

public class QueryBatchResponse
{
    /// <summary>
    /// Name of this result when used in a batch-collection
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    /// When true, the targetDrive queried for this section was not accessible due to permissions or did not exist
    /// </summary>
    public bool InvalidDrive { get; set; }

    /// <summary>
    /// Indicates when this result was generated
    /// </summary>
    public UnixTimeUtc QueryTime { get; set; }

    public bool IncludeMetadataHeader { get; set; }
    public string CursorState { get; set; }
    public bool HasMoreRows { get; set; }

    public IEnumerable<SharedSecretEncryptedFileHeader> SearchResults { get; set; }

    public static QueryBatchResponse FromResult(QueryBatchResult batch)
    {
        var response = new QueryBatchResponse()
        {
            QueryTime = batch.QueryTime,
            IncludeMetadataHeader = batch.IncludeMetadataHeader,
            CursorState = batch.Cursor.ToJson(),
            SearchResults = batch.SearchResults ?? new List<SharedSecretEncryptedFileHeader>(),
            HasMoreRows = batch.HasMoreRows
        };

        return response;
    }


    public static QueryBatchResponse FromInvalidDrive(string name)
    {
        return new QueryBatchResponse()
        {
            Name = name,
            InvalidDrive = true,
            SearchResults = new List<SharedSecretEncryptedFileHeader>()
        };
    }
}