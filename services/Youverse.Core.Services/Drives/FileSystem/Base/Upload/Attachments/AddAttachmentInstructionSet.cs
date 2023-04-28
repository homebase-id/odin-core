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

        /// <summary>
        /// The transfer initialization vector used to encrypt the KeyHeader
        /// </summary>
        public byte[] TransferIv { get; set; }

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
            if (null == TransferIv || ByteArrayUtil.EquiByteArrayCompare(TransferIv, Guid.Empty.ToByteArray()))
            {
                throw new YouverseClientException("Invalid or missing instruction set or transfer initialization vector",
                    YouverseClientErrorCode.InvalidInstructionSet);
            }

            if (!TargetFile.HasValue())
            {
                throw new YouverseClientException("OverwriteFile is invalid", YouverseClientErrorCode.InvalidFile);
            }
        }
    }
}