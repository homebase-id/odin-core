using System.Collections.Generic;

namespace Odin.Hosting.UnifiedV2.Drive.Write;

public class UpdateArchivalStatusBatchRequestV2
{
    /// <summary>
    /// Required. The archival status every file in the batch is set to (e.g. 0 to restore, 2 to remove). Nullable
    /// only so that a missing or misspelled field is a 400, not a silent 0 (restore).
    /// </summary>
    public int? ArchivalStatus { get; init; }

    /// <summary>
    /// The files to update, each with the version tag the caller last saw. At most
    /// <see cref="FileVersionTagBatchV2.MaxFiles"/>.
    /// </summary>
    public List<FileVersionTagV2> Files { get; init; }
}
