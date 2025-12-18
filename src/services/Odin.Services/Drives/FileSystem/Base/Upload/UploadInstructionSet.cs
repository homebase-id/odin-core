using System;
using System.Collections.Generic;
using System.Linq;
using Odin.Core;
using Odin.Core.Exceptions;
using Odin.Services.Peer;
using Odin.Services.Peer.Outgoing;
using Odin.Services.Util;
using Odin.Services.Peer.Outgoing.Drive;

namespace Odin.Services.Drives.FileSystem.Base.Upload
{
    /// <summary>
    /// Specifies how an upload should be handled
    /// </summary>
    public class UploadInstructionSet
    {
        public UploadInstructionSet()
        {
            TransitOptions = new TransitOptions();
            StorageOptions = new StorageOptions();
            Manifest = new UploadManifest();
        }

        /// <summary>
        /// The transfer initialization vector used to encrypt the KeyHeader
        /// </summary>
        public byte[] TransferIv { get; set; }

        public StorageOptions StorageOptions { get; set; }

        public TransitOptions TransitOptions { get; set; }
        
        public UploadManifest Manifest { get; set; }

        public void AssertIsValid()
        {
            if (null == TransferIv || ByteArrayUtil.EquiByteArrayCompare(TransferIv, Guid.Empty.ToByteArray()))
            {
                throw new OdinClientException("Invalid or missing instruction set or transfer initialization vector",
                    OdinClientErrorCode.InvalidInstructionSet);
            }

            // removed validation check here to allow v2 to send in the driveId via http path
            // if (!StorageOptions?.Drive?.IsValid() ?? false)
            // {
            //     throw new OdinClientException("Target drive is invalid", OdinClientErrorCode.InvalidTargetDrive);
            // }

            Manifest?.AssertIsValid();
        }

        public static UploadInstructionSet WithRecipients(TargetDrive drive, IEnumerable<string> recipients)
        {
            return WithRecipients(drive, recipients.ToArray());
        }

        public static UploadInstructionSet WithRecipients(TargetDrive drive, params string[] recipients)
        {
            return new UploadInstructionSet()
            {
                TransferIv = ByteArrayUtil.GetRndByteArray(16),
                StorageOptions = new StorageOptions()
                {
                    Drive = drive,
                    OverwriteFileId = null
                },
                TransitOptions = new TransitOptions()
                {
                    Recipients = recipients.ToList()
                },
                Manifest = new UploadManifest()
            };
        }

        public static UploadInstructionSet WithTargetDrive(TargetDrive drive)
        {
            return new UploadInstructionSet()
            {
                TransferIv = ByteArrayUtil.GetRndByteArray(16),
                StorageOptions = new StorageOptions()
                {
                    Drive = drive,
                    OverwriteFileId = null
                }
            };
        }
    }
}