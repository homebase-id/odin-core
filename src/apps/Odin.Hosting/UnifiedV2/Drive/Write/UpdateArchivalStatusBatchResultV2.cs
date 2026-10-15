using System;
using System.Collections.Generic;
using Odin.Core.Exceptions;

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
    /// The file's version tag after the update; null when <see cref="ErrorCode"/> is set.
    /// </summary>
    public Guid? NewVersionTag { get; init; }

    /// <summary>
    /// Null on success. Otherwise why this file was not updated: 4160 VersionTagMismatch (stale tag),
    /// 4106 FileNotFound, or 4002 CannotUpdateNonActiveFile (deleted).
    /// </summary>
    public OdinClientErrorCode? ErrorCode { get; init; }
}
