using System;
using System.Collections.Generic;
using System.Linq;
using Dawn;
using Odin.Core.Exceptions;
using Odin.Core.Services.Peer.SendingHost;

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

            if (Manifest?.PayloadDescriptors?.Any() ?? false)
            {
                foreach (var pd in Manifest.PayloadDescriptors)
                {
                    DriveFileUtility.AssertValidPayloadKey(pd.PayloadKey);

                    var anyMissingThumbnailKey = pd.Thumbnails?.Any(thumb => string.IsNullOrEmpty(thumb.ThumbnailKey?.Trim())) ?? false;
                    if (anyMissingThumbnailKey)
                    {
                        throw new OdinClientException($"The payload key [{pd.PayloadKey}] as a thumbnail missing a thumbnailKey",
                            OdinClientErrorCode.InvalidUpload);
                    }

                    // the width and height of all thumbnails must be unique for a given payload key
                    var hasDuplicates = pd.Thumbnails.GroupBy(p => $"{p.PixelWidth}{p.PixelHeight}")
                        .Any(group => group.Count() > 1);

                    if (hasDuplicates)
                    {
                        throw new OdinClientException($"You have duplicate thumbnails for the " +
                                                      $"payloadKey [{pd.PayloadKey}]. in the UploadManifest. " +
                                                      $"You can have multiple thumbnails per payload key, however, " +
                                                      $"the WidthXHeight must be unique.", OdinClientErrorCode.InvalidPayload);
                    }
                }
            }

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
                },
                Manifest = new UploadManifest()
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