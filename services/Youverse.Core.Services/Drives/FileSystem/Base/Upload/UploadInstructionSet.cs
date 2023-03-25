using System;
using System.Collections.Generic;
using System.Linq;
using Dawn;
using Youverse.Core.Exceptions;
using Youverse.Core.Services.Transit;
using Youverse.Core.Services.Transit.SendingHost;

namespace Youverse.Core.Services.Drives.FileSystem.Base.Upload
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
        }

        /// <summary>
        /// The transfer initialization vector used to encrypt the KeyHeader
        /// </summary>
        public byte[] TransferIv { get; set; }

        public StorageOptions StorageOptions { get; set; }

        public TransitOptions TransitOptions { get; set; }

        public void AssertIsValid()
        {
            if (null == TransferIv || ByteArrayUtil.EquiByteArrayCompare(TransferIv, Guid.Empty.ToByteArray()))
            {
                throw new YouverseClientException("Invalid or missing instruction set or transfer initialization vector", YouverseClientErrorCode.InvalidInstructionSet);
            }

            if (!StorageOptions?.Drive?.IsValid() ?? false)
            {
                throw new YouverseClientException("Target drive is invalid", YouverseClientErrorCode.InvalidTargetDrive);
            }

            //Removed because this conflicts with AllowDistribution flag.
            //Having UseGlobalTransitId with a transient file does not hurt anything;  
            //it's just illogical because the file is going to be deleted
            // if (TransitOptions != null)
            // {
            //     if (TransitOptions.IsTransient && TransitOptions.UseGlobalTransitId)
            //     {
            //         throw new YouverseClientException("Cannot use GlobalTransitId on a transient file.", YouverseClientErrorCode.CannotUseGlobalTransitIdOnTransientFile);
            //     }
            // }
        }
        
        public static UploadInstructionSet WithRecipients(TargetDrive drive, IEnumerable<string> recipients)
        {
            return WithRecipients(drive, recipients.ToArray());
        }

        public static UploadInstructionSet WithRecipients(TargetDrive drive, params string[] recipients)
        {
            Guard.Argument(drive, nameof(drive)).NotNull();
            Guard.Argument(recipients, nameof(recipients)).NotNull().NotEmpty();

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
                }
            };
        }

        public static UploadInstructionSet WithTargetDrive(TargetDrive drive)
        {
            Guard.Argument(drive, nameof(drive)).NotNull();

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