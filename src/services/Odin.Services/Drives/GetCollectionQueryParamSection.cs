using System;
using Odin.Core.Storage.Database.Identity.Abstractions;
using Odin.Core.Time;
using Odin.Services.Drives.DriveCore.Query;
using Odin.Services.Drives.DriveCore.Storage;

namespace Odin.Services.Drives;

public class GetCollectionQueryParamSection {
    public string Name { get; set; }


    // FileQueryParams

    public Guid Alias { get; set; }
    public Guid Type { get; set; }

    public int[] FileType { get; set; } = null;
    public FileState[] FileState { get; set; } = null;
    
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

    public CollectionQueryParamSection ToCollectionQueryParamSection () {
        return new CollectionQueryParamSection() {
            Name = this.Name,
            QueryParams = new FileQueryParams() {
                TargetDrive = new TargetDrive() {
                    Alias = this.Alias,
                    Type = this.Type,
                },
                FileType = this.FileType,
                FileState = this.FileState,
                DataType = this.DataType,
                ArchivalStatus = this.ArchivalStatus,
                Sender = this.Sender,
                GroupId = this.GroupId,
                UserDate = this.UserDateStart != null && this.UserDateEnd != null ? new UnixTimeUtcRange((UnixTimeUtc) this.UserDateStart.Value,(UnixTimeUtc) this.UserDateEnd.Value) : null,
                ClientUniqueIdAtLeastOne = this.ClientUniqueIdAtLeastOne,
                TagsMatchAtLeastOne = this.TagsMatchAtLeastOne,
                TagsMatchAll = this.TagsMatchAll,
                GlobalTransitId = this.GlobalTransitId
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