using System;
using System.Collections.Generic;
using Odin.Core.Time;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.FileSystem.Base;

#nullable enable

namespace Odin.Services.Registry.PayloadMove;

// What the target and the source's payload move endpoint exchange

public static class PayloadMoveProtocol
{
    public const string RootPath = "/api/payload-move";

    public static string IdentityPath(Guid identityId) => $"{RootPath}/v1/{identityId}";
}

public class PayloadMoveRedeemRequest
{
    public string? HandoffToken { get; set; }
}

public class PayloadMoveRedeemResponse
{
    public string Credential { get; set; } = "";
}

/// <summary>
/// One stored object of a file: a payload, or one of its thumbnails when <see cref="Width"/> is set.
/// Addressed by what it is, so each host builds its own path. <see cref="ExpectedLength"/> is the byte
/// count the header records, or 0 if it records none.
/// </summary>
public sealed record PayloadObject(
    Guid DriveId,
    Guid FileId,
    string Key,
    UnixTimeUtcUnique Uid,
    long ExpectedLength,
    int Width = 0,
    int Height = 0)
{
    public bool IsThumbnail => Width > 0;

    /// <summary>A file's stored objects: each payload and its thumbnails. A file whose payloads live elsewhere has none.</summary>
    public static IEnumerable<PayloadObject> AllOf(Guid driveId, Guid fileId, FileMetadata? metadata)
    {
        if (metadata?.Payloads == null || metadata.PayloadsAreRemote)
        {
            yield break;
        }

        foreach (var payload in metadata.Payloads)
        {
            yield return new PayloadObject(driveId, fileId, payload.Key, payload.Uid, payload.BytesWritten);
            foreach (var thumbnail in payload.Thumbnails ?? [])
            {
                yield return new PayloadObject(driveId, fileId, payload.Key, payload.Uid, thumbnail.BytesWritten,
                    thumbnail.PixelWidth, thumbnail.PixelHeight);
            }
        }
    }

    /// <summary>Where this host keeps it: each host builds its own path from what the object is.</summary>
    public string PathIn(TenantPathManager paths) => IsThumbnail
        ? paths.GetThumbnailDirectoryAndFileName(DriveId, FileId, Key, Uid, Width, Height)
        : paths.GetPayloadDirectoryAndFileName(DriveId, FileId, Key, Uid);

    public string SourcePath(Guid identityId) => IsThumbnail
        ? $"{PayloadMoveProtocol.IdentityPath(identityId)}/thumb/{DriveId}/{FileId}/{Key}/{Uid.uniqueTime}/{Width}x{Height}"
        : $"{PayloadMoveProtocol.IdentityPath(identityId)}/payload/{DriveId}/{FileId}/{Key}/{Uid.uniqueTime}";

    public override string ToString() => IsThumbnail
        ? $"thumbnail {Width}x{Height} of {Key} ({Uid.uniqueTime}) in file {FileId}"
        : $"payload {Key} ({Uid.uniqueTime}) in file {FileId}";
}
