using System;
using System.Text.Json.Serialization;
using Odin.Core.Time;
using Odin.Services.Drives.DriveCore.Storage;

namespace Odin.Services.Drives;

#nullable enable

/// <summary>
/// What identifies a file and its version, without the header: a row of a <see cref="FileResultShape.Minimal"/>
/// query-batch result, and the file of a drive event on a socket that asked for that shape.  The names and JSON types match the client file header (fileState is a camelCase
/// string, as there).  Optional values that are absent are left out of the JSON, not sent as null; fileState and
/// archivalStatus are always sent, as their default values mean something.
/// </summary>
public class FileRef
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
    /// The header's version tag: it changes when the header is written, not on reaction or transfer-history
    /// updates, which move only <see cref="Updated"/>
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

    /// <summary>
    /// Moves on every change that moves the file along the change feed
    /// </summary>
    public UnixTimeUtc Updated { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SenderOdinId { get; init; }

    public long FileByteCount { get; init; }

    public static FileRef FromHeader(ServerFileHeader header)
    {
        var metadata = header.FileMetadata;
        var localVersionTag = metadata.LocalAppData?.VersionTag;
        return new FileRef
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
