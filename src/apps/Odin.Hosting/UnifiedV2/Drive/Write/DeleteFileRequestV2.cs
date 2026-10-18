using System;
using System.Collections.Generic;

namespace Odin.Hosting.UnifiedV2.Drive.Write;

public class DeleteFileRequestV2
{
    /// <summary>
    /// The file to be deleted
    /// </summary>
    public Guid FileId { get; set; }

    /// <summary>
    /// List of recipients to receive the delete-file notification
    /// </summary>
    public List<string> Recipients { get; set; }

    /// <summary>
    /// Optional. When set, the file is only deleted if it is still at this version. Otherwise a single delete fails
    /// with 4160 VersionTagMismatch, and in a batch that file's result has errorCode "versionTagMismatch" while the
    /// others are deleted.
    /// </summary>
    public Guid? VersionTag { get; set; }
}