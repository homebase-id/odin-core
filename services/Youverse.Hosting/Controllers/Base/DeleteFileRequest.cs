using System.Collections.Generic;
using Youverse.Core.Services.Drives;

namespace Youverse.Hosting.Controllers.Base;

public class DeleteFileRequest
{
    /// <summary>
    /// The file to be deleted
    /// </summary>
    public ExternalFileIdentifier File { get; set; }

    /// <summary>
    /// If the file has a GlobalTransitId, all Recipients will receive a notification to delete the file
    /// </summary>
    public bool DeleteLinkedFiles { get; set; }

    /// <summary>
    /// List of recipients to receive the delete-file notification
    /// </summary>
    public List<string> Recipients { get; set; }
}

public class DeleteAttachmentRequest
{
    public string Key { get; set; }

    public ExternalFileIdentifier File { get; set; }

    public AttachmentType Type { get; set; }

    // public int Width { get; set; }
    // public int Height { get; set; }
}

public enum AttachmentType
{
    Thumbnail = 3,
    Payload = 1
}