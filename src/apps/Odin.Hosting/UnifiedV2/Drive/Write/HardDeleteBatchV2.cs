using System;
using System.Collections.Generic;
using Odin.Core.Exceptions;

namespace Odin.Hosting.UnifiedV2.Drive.Write;

public class HardDeleteBatchRequestV2
{
    /// <summary>
    /// The files to delete for good, each with the version tag the caller last saw. At most
    /// <see cref="FileVersionTagBatchV2.MaxFiles"/>.
    /// </summary>
    public List<FileVersionTagV2> Files { get; init; }
}

public class HardDeleteBatchResultV2
{
    /// <summary>
    /// One entry per requested file, in request order.
    /// </summary>
    public List<HardDeleteResultV2> Results { get; init; }
}

public class HardDeleteResultV2
{
    public Guid FileId { get; init; }

    /// <summary>
    /// Null when the file is gone. Otherwise why it was left alone, serialized as a camelCase string like every
    /// OdinClientErrorCode: "versionTagMismatch" (written since the caller read it), "fileNotFound" (missing, or
    /// hidden from the caller by its ACL), or any other client error. One file's error never stops the others.
    /// </summary>
    public OdinClientErrorCode? ErrorCode { get; init; }
}
