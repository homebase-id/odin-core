using Youverse.Core.Services.Drives.DriveCore.Query;
using System;
using System.Diagnostics;
using System.Linq;
using System.Collections.Generic;
using Dawn;

namespace Youverse.Core.Services.Drives;

public class QueryBatchRequest
{
    public FileQueryParams QueryParams { get; set; }

    public QueryBatchResultOptionsRequest ResultOptionsRequest { get; set; }
}

public class GetQueryBatchRequest
{
    // FileQueryParams

    public Guid Alias { get; set; }
    public Guid Type { get; set; }

    public IEnumerable<int> FileType { get; set; } = null;
    public IEnumerable<int> DataType { get; set; } = null;

    public IEnumerable<int> ArchivalStatus { get; set; } = null;

    /// <summary>
    /// List of byte[] where the content is a lower-cased UTF8 encoded byte array of the identity.
    /// </summary>
    public IEnumerable<byte[]> Sender { get; set; } = null;

    public IEnumerable<Guid> GroupId { get; set; } = null;

    public Nullable<UnixTimeUtc> UserDateStart { get; set; } = null;
    public Nullable<UnixTimeUtc> UserDateEnd { get; set; } = null;

    public IEnumerable<Guid> ClientUniqueIdAtLeastOne { get; set; } = null;
    public IEnumerable<Guid> TagsMatchAtLeastOne { get; set; } = null;


    // QueryBatchResultOptionsRequest

    public string CursorState { get; set; }

    /// <summary>
    /// Max number of records to return
    /// </summary>
    public int MaxRecords { get; set; } = 100;

    /// <summary>
    /// Specifies if the result set includes the metadata header (assuming the file has one)
    /// </summary>
    public bool IncludeMetadataHeader { get; set; }

    public Ordering Ordering { get; set; }

    public Sorting Sorting { get; set; }

    public QueryBatchRequest toQueryBatchRequest () {
        return new QueryBatchRequest() {
            QueryParams = new FileQueryParams() {
                TargetDrive = new TargetDrive() {
                    Alias = this.Alias,
                    Type = this.Type,
                },
                FileType = this.FileType,
                DataType = this.DataType,
                ArchivalStatus = this.ArchivalStatus,
                Sender = this.Sender,
                GroupId = this.GroupId,
                // TODO: C# magic to find out how to do this:
                // UserDate = this.UserDateStart != null && this.UserDateEnd != null ? new UnixTimeUtcRange(this.UserDateStart as Youverse.Core.UnixTimeUtc, this.UserDateEnd as Youverse.Core.UnixTimeUtc) : null,
                ClientUniqueIdAtLeastOne = this.ClientUniqueIdAtLeastOne,
                TagsMatchAtLeastOne = this.TagsMatchAtLeastOne,
            },
            ResultOptionsRequest = new QueryBatchResultOptionsRequest() {
                CursorState = this.CursorState,
                MaxRecords = this.MaxRecords,
                IncludeMetadataHeader = this.IncludeMetadataHeader,
                Ordering = this.Ordering,
                Sorting = this.Sorting,
            }
        };
    }
}
