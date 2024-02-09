using System;
using System.Collections.Generic;
using System.Linq;

using Odin.Core.Exceptions;
using Odin.Core.Services.Peer;
using Odin.Core.Services.Peer.Outgoing;
using Odin.Core.Services.Peer.Outgoing.Drive;
using Odin.Core.Services.Util;

namespace Odin.Core.Services.Drives.FileSystem.Base.Upload
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

            if (!StorageOptions?.Drive?.IsValid() ?? false)
            {
                throw new OdinClientException("Target drive is invalid", OdinClientErrorCode.InvalidTargetDrive);
            }

            Manifest?.AssertIsValid();

            //Removed because this conflicts with AllowDistribution flag.
            //Having UseGlobalTransitId with a transient file does not hurt anything;  
            //it's just illogical because the file is going to be deleted
            // if (TransitOptions != null)
            // {
            //     if (TransitOptions.IsTransient && TransitOptions.UseGlobalTransitId)
            //     {
            //         throw new OdinClientException("Cannot use GlobalTransitId on a transient file.", OdinClientErrorCode.CannotUseGlobalTransitIdOnTransientFile);
            //     }
            // }
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