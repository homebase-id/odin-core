using System;
using Odin.Core.Storage;
using Odin.Services.Drives;
using Odin.Services.Drives.FileSystem.Base.Upload;

namespace Odin.Services.Peer.Outgoing.Drive.Transfer;

public class PayloadTransferInstructionSet
{
    public GlobalTransitIdFileIdentifier TargetFile { get; set; }

    public Guid VersionTag { get; set; }

    public UploadManifest Manifest { get; set; }

    public AppNotificationOptions AppNotificationOptions { get; set; }

    public FileSystemType FileSystemType { get; set; }
}