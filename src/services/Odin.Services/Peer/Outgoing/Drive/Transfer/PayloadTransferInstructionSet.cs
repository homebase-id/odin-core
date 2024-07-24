using System;
using Odin.Core.Exceptions;
using Odin.Core.Storage;
using Odin.Services.Base;
using Odin.Services.Drives.FileSystem.Base.Upload;
using Odin.Services.Util;

namespace Odin.Services.Peer.Outgoing.Drive.Transfer;

public class PayloadTransferInstructionSet
{
    public FileIdentifier TargetFile { get; set; }

    public Guid VersionTag { get; set; }

    public UploadManifest Manifest { get; set; }

    public AppNotificationOptions AppNotificationOptions { get; set; }

    public FileSystemType FileSystemType { get; set; }

    public void AssertIsValid()
    {
        OdinValidationUtils.AssertNotNull(Manifest, "Manifest is required");
        
        TargetFile.AssertIsValid();
        Manifest.AssertIsValid();

        if ((int)FileSystemType == 0)
        {
            throw new OdinClientException("The FileSystemType is invalid");
        }
    }
}