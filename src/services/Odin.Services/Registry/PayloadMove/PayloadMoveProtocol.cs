using System;
using Odin.Core.Time;

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

    public string SourcePath(Guid identityId) => IsThumbnail
        ? $"{PayloadMoveProtocol.IdentityPath(identityId)}/thumb/{DriveId}/{FileId}/{Key}/{Uid.uniqueTime}/{Width}x{Height}"
        : $"{PayloadMoveProtocol.IdentityPath(identityId)}/payload/{DriveId}/{FileId}/{Key}/{Uid.uniqueTime}";

    public override string ToString() => IsThumbnail
        ? $"thumbnail {Width}x{Height} of {Key} ({Uid.uniqueTime}) in file {FileId}"
        : $"payload {Key} ({Uid.uniqueTime}) in file {FileId}";
}
