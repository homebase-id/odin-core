using System;
using System.Text.Json.Serialization;
using Odin.Core.Time;
using Odin.Services.Drives.DriveCore.Storage;

namespace Odin.Services.Drives;

#nullable enable

/// <summary>
/// One row of a <see cref="QueryBatchResultShape.Minimal"/> query-batch result: what identifies a file and its
/// version, without the header.  The names match the client file header.  Optional values that are absent are
/// left out of the JSON, not sent as null; numbers that mean something at 0 (fileState, archivalStatus) are
/// always sent.
/// </summary>
public class QueryBatchFileRef
{
    public Guid FileId { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Guid? GlobalTransitId { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Guid? UniqueId { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Guid? GroupId { get; init; }

    public FileState FileState { get; init; }
    public int FileType { get; init; }
    public int DataType { get; init; }
    public int ArchivalStatus { get; init; }

    /// <summary>
    /// The header's version tag; it changes on every write to the file
    /// </summary>
    public Guid VersionTag { get; init; }

    /// <summary>
    /// The version tag of the file's local app data; absent if it was never written
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Guid? LocalVersionTag { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public UnixTimeUtc? UserDate { get; init; }

    public UnixTimeUtc Created { get; init; }
    public UnixTimeUtc Updated { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SenderOdinId { get; init; }

    public long FileByteCount { get; init; }

    public static QueryBatchFileRef FromHeader(ServerFileHeader header)
    {
        var metadata = header.FileMetadata;
        var localVersionTag = metadata.LocalAppData?.VersionTag;
        return new QueryBatchFileRef
        {
            FileId = metadata.File.FileId,
            GlobalTransitId = metadata.GlobalTransitId,
            UniqueId = metadata.AppData?.UniqueId,
            GroupId = metadata.AppData?.GroupId,
            FileState = metadata.FileState,
            FileType = metadata.AppData?.FileType ?? 0,
            DataType = metadata.AppData?.DataType ?? 0,
            ArchivalStatus = metadata.AppData?.ArchivalStatus ?? 0,
            VersionTag = metadata.VersionTag.GetValueOrDefault(),
            LocalVersionTag = localVersionTag == Guid.Empty ? null : localVersionTag,
            UserDate = metadata.AppData?.UserDate,
            Created = metadata.Created,
            Updated = metadata.Updated,
            SenderOdinId = string.IsNullOrEmpty(metadata.SenderOdinId) ? null : metadata.SenderOdinId,
            FileByteCount = header.ServerMetadata?.FileByteCount ?? 0
        };
    }
}
