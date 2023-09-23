using System;
using Odin.Core.Services.Drives.DriveCore.Query;
using Odin.Core.Time;

namespace Odin.Core.Services.Drives;

public class QueryModifiedRequest
{
    public FileQueryParams QueryParams { get; set; }
    public QueryModifiedResultOptions ResultOptions { get; set; }
}

public class GetQueryModifiedRequest
{
    // FileQueryParams
    public Guid Alias { get; set; }
    public Guid Type { get; set; }

    public int[] FileType { get; set; } = null;
    public int[] DataType { get; set; } = null;

    public int[] ArchivalStatus { get; set; } = null;

    /// <summary>
    /// List of byte[] where the content is a lower-cased UTF8 encoded byte array of the identity.
    /// </summary>
    public byte[][] Sender { get; set; } = null;

    public Guid[] GroupId { get; set; } = null;

    public Int64? UserDateStart { get; set; } = null;
    public Int64? UserDateEnd { get; set; } = null;

    public Guid[] ClientUniqueIdAtLeastOne { get; set; } = null;
    public Guid[] TagsMatchAtLeastOne { get; set; } = null;

    public Guid[] TagsMatchAll { get; set; } = null;

    public Guid[] GlobalTransitId { get; set; } = null;

    // QueryModifiedResultOptions
    public long MaxDate { get; set; }
    public long Cursor { get; set; }

    /// <summary>
    /// Max number of records to return
    /// </summary>
    public int MaxRecords { get; set; } = 100;

    public bool IncludeJsonContent { get; set; }
    public bool ExcludePreviewThumbnail { get; set; }

     public QueryModifiedRequest ToQueryModifiedRequest () {
        return new QueryModifiedRequest() {
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
                UserDate = this.UserDateStart != null && this.UserDateEnd != null ? new UnixTimeUtcRange((UnixTimeUtc) this.UserDateStart.Value,(UnixTimeUtc) this.UserDateEnd.Value) : null,
                ClientUniqueIdAtLeastOne = this.ClientUniqueIdAtLeastOne,
                TagsMatchAtLeastOne = this.TagsMatchAtLeastOne,
                TagsMatchAll = this.TagsMatchAll,
                GlobalTransitId = this.GlobalTransitId,
            },
            ResultOptions = new QueryModifiedResultOptions() {
                MaxDate = this.MaxDate,
                Cursor = this.Cursor,
                MaxRecords = this.MaxRecords,
                IncludeJsonContent = this.IncludeJsonContent,
                ExcludePreviewThumbnail = this.ExcludePreviewThumbnail,
            }
        };
     }
}
