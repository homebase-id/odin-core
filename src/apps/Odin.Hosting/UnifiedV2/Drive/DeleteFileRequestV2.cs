using System;
using System.Collections.Generic;

namespace Odin.Hosting.UnifiedV2.Drive;

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
}