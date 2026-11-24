using System;
using System.Collections.Generic;
using Odin.Services.Drives;

namespace Odin.Hosting.UnifiedV2.Drive.Write;

public class DeleteFileOptionsV2  {
    public List<string> Recipients { get; init; }

    /// <summary>
    /// Optional. When set, the file is only deleted if it is still at this version; otherwise 4160
    /// VersionTagMismatch, and nothing is deleted or sent to recipients.
    /// </summary>
    public Guid? VersionTag { get; init; }

    /// <summary>
    /// Optional. What the tombstone keeps beyond the default, e.g. <c>{ "keepUniqueId": true }</c>; left out, the
    /// tombstone is as it always was. Recipients' copies get the default tombstone.
    /// </summary>
    public TombstoneOptions Tombstone { get; init; }
}