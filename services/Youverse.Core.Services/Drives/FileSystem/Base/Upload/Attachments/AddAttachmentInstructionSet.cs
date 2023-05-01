using System;
using System.Collections.Generic;
using Youverse.Core.Exceptions;
using Youverse.Core.Services.Drives.DriveCore.Storage;

namespace Youverse.Core.Services.Drives.FileSystem.Base.Upload.Attachments
{
    /// <summary>
    /// Specifies how an uploaded attachment (i.e. thumbnail or payload) should be handled
    /// </summary>
    public class AddAttachmentInstructionSet
    {
        public AddAttachmentInstructionSet()
        {
            TargetFile = new ExternalFileIdentifier();
        }

        public ExternalFileIdentifier TargetFile { get; set; }

        /// <summary>
        /// Thumbnails included in this attachment
        /// </summary>
        public IEnumerable<ImageDataHeader> Thumbnails { get; set; }
        
        /// <summary>
        /// Payloads included in this attachment
        /// </summary>
        // public IEnumerable<PayloadHeader> PayloadHeaders { get; set; }
        
        public void AssertIsValid()
        {
            if (!TargetFile.HasValue())
            {
                throw new YouverseClientException("OverwriteFile is invalid", YouverseClientErrorCode.InvalidFile);
            }
        }
    }
}