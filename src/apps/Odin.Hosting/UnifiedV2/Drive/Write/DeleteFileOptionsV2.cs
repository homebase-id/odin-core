using System;
using System.Collections.Generic;

namespace Odin.Hosting.UnifiedV2.Drive.Write;

public class DeleteFileOptionsV2  {
    public List<string> Recipients { get; init; }

    /// <summary>
    /// Optional. When set, the file is only deleted if it is still at this version; otherwise 4160
    /// VersionTagMismatch, and nothing is deleted or sent to recipients.
    /// </summary>
    public Guid? VersionTag { get; init; }
}