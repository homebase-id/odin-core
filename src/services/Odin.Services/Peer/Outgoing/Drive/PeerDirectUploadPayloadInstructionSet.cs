using System;
using System.Collections.Generic;
using System.Linq;
using Odin.Core.Exceptions;
using Odin.Services.Base;
using Odin.Services.Drives.FileSystem.Base.Upload;
using Odin.Services.Util;

namespace Odin.Services.Peer.Outgoing.Drive;

public class PeerDirectUploadPayloadInstructionSet
{
    public FileIdentifier TargetFile { get; set; }

    public UploadManifest Manifest { get; set; }

    /// <summary>
    /// List of identities that should receive this file 
    /// </summary>
    public List<string> Recipients { get; set; }

    public Guid VersionTag { get; set; }

    public void AssertIsValid()
    {
        if (null == Manifest)
        {
            throw new OdinClientException("Invalid Manifest");
        }
        
        OdinValidationUtils.AssertValidRecipientList(this.Recipients, false);
        if (Guid.Empty == this.TargetFile.FileId)
        {
            throw new OdinClientException("Invalid FileId");
        }

        if (!TargetFile.Drive.IsValid())
        {
            throw new OdinClientException("Remote Target Drive is invalid", OdinClientErrorCode.InvalidDrive);
        }

        if (!Manifest.PayloadDescriptors?.Any() ?? false)
        {
            throw new OdinClientException("Whatcha uploading buddy?  You're missing payloads when using the payload only upload method :)",
                OdinClientErrorCode.InvalidPayload);
        }

        Manifest.AssertIsValid();
    }
}