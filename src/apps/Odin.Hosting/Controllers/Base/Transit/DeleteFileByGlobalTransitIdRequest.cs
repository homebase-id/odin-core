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
    /// The version of the recipient's file the delete is meant for, optional.  The recipient refuses the delete
    /// (<c>VersionTagMismatch</c>) when its file has changed since, so it can't discard a newer version unseen.  Naming it
    /// also has the outcome told to the sending app's sockets (<c>PeerSendResult</c>, with <c>isDelete</c>), matched by
    /// this tag as <c>sentVersionTag</c>, as for an update.
    /// </summary>
    public Guid? ExpectedVersionTag { get; set; }
}