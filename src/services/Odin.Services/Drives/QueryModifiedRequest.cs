using System;
using Odin.Core.Time;
using Odin.Services.Drives.DriveCore.Query;

namespace Odin.Services.Drives;

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

    public string[] Sender { get; set; } = null;

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

    public bool IncludeHeaderContent { get; set; }
    public bool IncludeTransferHistory { get; set; }

    public bool ExcludePreviewThumbnail { get; set; }

    public QueryModifiedRequest ToQueryModifiedRequest()
    {
        return new QueryModifiedRequest()
        {
            QueryParams = new FileQueryParams()
            {
                TargetDrive = new TargetDrive()
                {
                    Alias = this.Alias,
                    Type = this.Type,
                },
                FileType = this.FileType,
                DataType = this.DataType,
                ArchivalStatus = this.ArchivalStatus,
                Sender = this.Sender,
                GroupId = this.GroupId,
                UserDate = this.UserDateStart != null && this.UserDateEnd != null ? new UnixTimeUtcRange((UnixTimeUtc)this.UserDateStart.Value, (UnixTimeUtc)this.UserDateEnd.Value) : null,
                ClientUniqueIdAtLeastOne = this.ClientUniqueIdAtLeastOne,
                TagsMatchAtLeastOne = this.TagsMatchAtLeastOne,
                TagsMatchAll = this.TagsMatchAll,
                GlobalTransitId = this.GlobalTransitId,
            },
            ResultOptions = new QueryModifiedResultOptions()
            {
                MaxDate = this.MaxDate,
                Cursor = this.Cursor,
                MaxRecords = this.MaxRecords,
                IncludeHeaderContent = this.IncludeHeaderContent,
                IncludeTransferHistory = this.IncludeTransferHistory,
                ExcludePreviewThumbnail = this.ExcludePreviewThumbnail,
            }
        };
    }
}
