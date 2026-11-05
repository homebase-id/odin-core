using System;
using Odin.Services.Base;
using Odin.Services.Drives.FileSystem.Base.Update;
using Odin.Services.Drives.FileSystem.Base.Upload;

namespace Odin.Services.Peer.Outgoing.Drive.Transfer;

public class UpdateRemoteFileRequest()
{
    public FileIdentifier File { get; init; }

    public UploadManifest Manifest { get; init; }

    public Guid NewVersionTag { get; init; }

    /// <summary>
    /// For an update of the recipient's copy only (<see cref="UpdateLocale.Peer"/>), the version tag that copy has, as the
    /// client named it: a member who may read it names it (docs/file-access.md, "Changing a file you sent").
    /// </summary>
    public Guid? ExpectedVersionTag { get; init; }

    public AppNotificationOptions AppNotificationOptions { get; init; }

    public UpdateLocale UpdateLocale { get; init; }
}