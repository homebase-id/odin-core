using System;
using System.Collections.Generic;

namespace Odin.Hosting.UnifiedV2.Drive.Write;

public class DeleteFileByGroupIdRequestV2
{
    /// <summary>
    /// The groupId of all files to be deleted
    /// </summary>
    public Guid GroupId { get; set; }

    /// <summary>
    /// List of recipients to receive the delete-file notification
    /// </summary>
    public List<string> Recipients { get; set; }
}