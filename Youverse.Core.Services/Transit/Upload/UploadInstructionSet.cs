using System;
using System.Collections.Generic;
using System.Linq;
using Dawn;
using Youverse.Core.Services.Drive;

namespace Youverse.Core.Services.Transit.Upload
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
                throw new UploadException("Invalid or missing instruction set or transfer initialization vector");
            }

            if (!StorageOptions?.Drive?.IsValid() ?? false)
            {
                throw new UploadException("Target drive is invalid");
            }

            if (TransitOptions != null)
            {
                if (TransitOptions.IsTransient && TransitOptions.UseGlobalTransitId)
                {
                    throw new UploadException("Cannot use GlobalTransitId on a transient file.");
                }
            }
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

        public static UploadInstructionSet New(TargetDrive drive)
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