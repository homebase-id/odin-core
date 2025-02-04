using System;
using System.Collections.Generic;
using System.Linq;
using Odin.Core.Exceptions;
using Odin.Services.Drives.DriveCore.Storage;

namespace Odin.Services.Drives.FileSystem.Base.Upload.Attachments
{
    /// <summary>
    /// Specifies how an uploaded payload should be handed; whether it's new or updating an existing payload
    /// </summary>
    public class UploadPayloadInstructionSet
    {
        public UploadPayloadInstructionSet()
        {
            TargetFile = new ExternalFileIdentifier();
        }

        public ExternalFileIdentifier TargetFile { get; set; }
        
        public UploadManifest Manifest { get; set; }

        /// <summary>
        /// List of identities that should receive this new payload
        /// </summary>
        public List<string> Recipients { get; set; }

        /// <summary>
        /// The version of the file you're to which you're uploading=
        /// </summary>
        public Guid? VersionTag { get; set; }

        public void AssertIsValid()
        {
            if (!TargetFile.HasValue())
            {
                throw new OdinClientException("Target File is invalid, you must indicate the file which will own the payload(s)", OdinClientErrorCode.InvalidFile);
            }

            if (!Manifest?.PayloadDescriptors?.Any() ?? false)
            {
                throw new OdinClientException("Whatcha uploading buddy?  You're missing payloads when using the payload only upload method :)", OdinClientErrorCode.InvalidPayload);
            }
            
            Manifest?.AssertIsValid();

        }
    }
}