using System;
using System.Collections.Generic;
using System.Linq;
using Dawn;
using Odin.Core.Services.Drives.DriveCore.Storage;
using Odin.Core.Time;

namespace Odin.Core.Services.Drives.FileSystem.Base.Upload
{
    /// <summary>
    /// A package/parcel to be send to a set of recipients
    /// </summary>
    public class FileUploadPackage
    {
        /// <summary />
        public FileUploadPackage(InternalDriveFileId internalFile, UploadInstructionSet instructionSet, bool isUpdateOperation)
        {
            Guard.Argument(internalFile, nameof(internalFile)).HasValue();
            Guard.Argument(internalFile.FileId, nameof(internalFile.FileId)).NotEqual(Guid.Empty);
            Guard.Argument(internalFile.DriveId, nameof(internalFile.DriveId)).NotEqual(Guid.Empty);

            // this.TempFile = internalFile with { FileId = Guid.NewGuid() };
            this.InternalFile = internalFile;
            this.InstructionSet = instructionSet;
            this.IsUpdateOperation = isUpdateOperation;
            this.Thumbnails = new List<PackageThumbnailDescriptor>();
            this.Payloads = new List<PackagePayloadDescriptor>();
        }

        /// <summary>
        /// The file to whi This is not the same as the
        /// target file to which the payloads will be attach
        /// </summary>
        //TODO
        // public InternalDriveFileId TempFile { get; set; }

        public UploadInstructionSet InstructionSet { get; init; }

        public InternalDriveFileId InternalFile { get; init; }

        public bool IsUpdateOperation { get; init; }

        /// <summary>
        /// List of payloads uploaded
        /// </summary>
        public List<PackagePayloadDescriptor> Payloads { get; }

        /// <summary>
        /// A list of thumbnails sent in the stream.
        /// this exists because payloads and thumbnails can
        /// be sent in any order (otherwise we would drop them
        /// in the payloads collection)
        /// </summary>
        public List<PackageThumbnailDescriptor> Thumbnails { get; }

        /// <summary>
        /// Merges uploaded payloads and thumbnails
        /// </summary>
        public List<PayloadDescriptor> GetFinalPayloadDescriptors()
        {
            var descriptors = Payloads.Select(p =>
            {
                var thumbnails = this.Thumbnails?.Where(t => t.PayloadKey == p.PayloadKey)
                    .Select(t => new ThumbnailDescriptor()
                    {
                        ContentType = t.ContentType,
                        PixelHeight = t.PixelHeight,
                        PixelWidth = t.PixelWidth,
                    }).ToList();

                return new PayloadDescriptor()
                {
                    Iv = p.Iv,
                    Uid = p.Uid,
                    Key = p.PayloadKey,
                    ContentType = p.ContentType,
                    Thumbnails = thumbnails,
                    LastModified = UnixTimeUtc.Now(),
                    BytesWritten = p.BytesWritten,
                    DescriptorContent = p.DescriptorContent,
                    PreviewThumbnail = p.PreviewThumbnail
                };
            });

            return descriptors.ToList();
        }

        public List<PackagePayloadDescriptor> GetPayloadsWithValidIVs()
        {
            return Payloads.Where(p => p.HasIv()).ToList();
        }
    }
}