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

    public AppNotificationOptions AppNotificationOptions { get; init; }

    public UpdateLocale UpdateLocale { get; init; }
}