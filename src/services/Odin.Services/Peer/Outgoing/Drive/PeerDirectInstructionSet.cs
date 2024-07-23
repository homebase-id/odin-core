using System;
using System.Collections.Generic;
using System.Linq;
using Odin.Core.Exceptions;
using Odin.Services.Drives;
using Odin.Services.Drives.FileSystem.Base.Upload;
using Odin.Services.Util;

namespace Odin.Services.Peer.Outgoing.Drive
{
    /// <summary>
    /// Specifies what to do with a file when it is uploaded to be sent directly to the recipients
    /// </summary>
    public class PeerDirectInstructionSet
    {
        /// <summary>
        /// The transfer initialization vector used to encrypt the KeyHeader
        /// </summary>
        public byte[] TransferIv { get; set; }
        
        /// <summary>
        /// List of identities that should receive this file 
        /// </summary>
        public List<string> Recipients { get; set; }
        
        /// <summary>
        /// The target drive on the recipient's identity; required
        /// </summary>
        public TargetDrive RemoteTargetDrive { get; set; }
        
        /// <summary>
        /// Optionally specified if you are overwriting a remote file
        /// </summary>
        public Guid? OverwriteGlobalTransitFileId { get; set; }

        public UploadManifest Manifest { get; set; }
        
        public StorageIntent StorageIntent { get; set; }
    }
    
    public class PeerUploadPayloadInstructionSet
    {
       
        /// <summary>
        /// List of identities that should receive this file 
        /// </summary>
        public List<string> Recipients { get; set; }
        
        /// <summary>
        /// The target drive on the recipient's identity; required
        /// </summary>
        public TargetDrive RemoteTargetDrive { get; set; }
        
        /// <summary>
        /// The globalTransitId of the target file on the remote identity
        /// </summary>
        public Guid OverwriteGlobalTransitFileId { get; set; }

        public UploadManifest Manifest { get; set; }
        
        public Guid VersionTag { get; set; }
        
        public void AssertIsValid()
        {
            OdinValidationUtils.AssertValidRecipientList(this.Recipients);
            if (Guid.Empty == OverwriteGlobalTransitFileId)
            {
                throw new OdinClientException("Invalid GlobalTransitFile Id");
            }
            
            if (!RemoteTargetDrive.IsValid())
            {
                throw new OdinClientException("Remote Target Drive is invalid", OdinClientErrorCode.InvalidDrive);
            }

            if (!Manifest?.PayloadDescriptors?.Any() ?? false)
            {
                throw new OdinClientException("Whatcha uploading buddy?  You're missing payloads when using the payload only upload method :)", OdinClientErrorCode.InvalidPayload);
            }
            
            Manifest?.AssertIsValid();

        }
    }
}