using System;
using System.Collections.Generic;
using Odin.Services.Drives;
using Odin.Core.Storage;

namespace Odin.Hosting.Controllers.Base.Transit;

public class DeleteFileByGlobalTransitIdRequest
{
    public FileSystemType FileSystemType { get; set; }

    /// <summary>
    /// The file to be deleted
    /// </summary>
    public GlobalTransitIdFileIdentifier GlobalTransitIdFileIdentifier { get; set; }

    /// <summary>
    /// List of recipients to receive the delete-file notification
    /// </summary>
    public List<string> Recipients { get; set; }

    /// <summary>
    /// Optional: the version of the recipient's file the delete is meant for, refused once the file has changed, with the
    /// outcome told as a <c>PeerSendResult</c> (see the V2 <c>senddeleterequest</c> route)
    /// </summary>
    public Guid? ExpectedVersionTag { get; set; }
}