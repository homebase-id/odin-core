using System;
using System.Collections.Generic;

namespace Odin.Hosting.UnifiedV2.Drive.Write;

public class UpdateArchivalStatusBatchRequestV2
{
    public const int MaxFiles = 1000;

    /// <summary>
    /// The archival status every file in the batch is set to (e.g. 0 to restore, 2 to remove).
    /// </summary>
    public int ArchivalStatus { get; init; }

    /// <summary>
    /// The files to update, each with the version tag the caller last saw. At most <see cref="MaxFiles"/>.
    /// </summary>
    public List<UpdateArchivalStatusFileV2> Files { get; init; }
}

public class UpdateArchivalStatusFileV2
{
    public Guid FileId { get; init; }

    /// <summary>
    /// Required. The file is only updated if it is still at this version; otherwise that file's result
    /// carries 4160 VersionTagMismatch and the rest of the batch goes ahead.
    /// </summary>
    public Guid VersionTag { get; init; }
}
