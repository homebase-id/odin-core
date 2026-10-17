using System;
using System.Collections.Generic;
using System.Linq;
using Odin.Services.Util;

namespace Odin.Hosting.UnifiedV2.Drive.Write;

/// <summary>
/// One file of a batch write, with the version tag the caller last saw
/// </summary>
public class FileVersionTagV2
{
    public Guid FileId { get; init; }

    /// <summary>
    /// Required. The file is only written if it is still at this version; otherwise that file's result carries
    /// errorCode "versionTagMismatch" and the rest of the batch goes ahead.
    /// </summary>
    public Guid VersionTag { get; init; }
}

public static class FileVersionTagBatchV2
{
    public const int MaxFiles = 1000;

    /// <summary>
    /// 1 to <see cref="MaxFiles"/> files, each with a file id and a version tag, none twice
    /// </summary>
    public static void AssertValid(List<FileVersionTagV2> files)
    {
        OdinValidationUtils.AssertNotNull(files, "files");
        OdinValidationUtils.AssertIsTrue(files.Count is > 0 and <= MaxFiles, $"files must hold 1 to {MaxFiles} entries");
        OdinValidationUtils.AssertIsTrue(files.All(f => f != null && f.FileId != Guid.Empty && f.VersionTag != Guid.Empty),
            "every file needs a fileId and a versionTag");
        OdinValidationUtils.AssertIsTrue(files.Select(f => f.FileId).Distinct().Count() == files.Count,
            "a file may appear only once");
    }
}
