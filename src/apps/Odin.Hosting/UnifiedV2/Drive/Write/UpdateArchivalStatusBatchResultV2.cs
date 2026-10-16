using System;
using System.Collections.Generic;
using Odin.Core.Exceptions;
using Odin.Core.Time;

namespace Odin.Hosting.UnifiedV2.Drive.Write;

public class UpdateArchivalStatusBatchResultV2
{
    /// <summary>
    /// One entry per requested file, in request order.
    /// </summary>
    public List<UpdateArchivalStatusResultV2> Results { get; init; }
}

public class UpdateArchivalStatusResultV2
{
    public Guid FileId { get; init; }

    /// <summary>
    /// The file's version tag after the update (unchanged when it was already at the requested status); null when
    /// <see cref="ErrorCode"/> is set.
    /// </summary>
    public Guid? NewVersionTag { get; init; }

    /// <summary>
    /// The file's modified time after the update, as the change feed shows it; null when <see cref="ErrorCode"/> is set.
    /// </summary>
    public UnixTimeUtc? Updated { get; init; }

    /// <summary>
    /// Null on success. Otherwise why this file was not updated, serialized as a camelCase string like every
    /// OdinClientErrorCode: "versionTagMismatch" (stale tag), "fileNotFound" (missing, or hidden from the caller by
    /// its ACL, as a query would leave it out), "cannotUpdateNonActiveFile" (deleted), or any other client error the
    /// header write raised. One file's error never stops the others.
    /// </summary>
    public OdinClientErrorCode? ErrorCode { get; init; }
}
