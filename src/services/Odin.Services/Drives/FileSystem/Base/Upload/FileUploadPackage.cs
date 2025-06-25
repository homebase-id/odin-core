using System.Collections.Generic;
using System.Linq;
using Odin.Core.Time;
using Odin.Services.Drives.DriveCore.Storage;

namespace Odin.Services.Drives.FileSystem.Base.Upload
{
    /// <summary>
    /// A package/parcel to be sent to a set of recipients
    /// </summary>
    public class FileUploadPackage
    {
        /// <summary />
        public FileUploadPackage(InternalDriveFileId internalFile, UploadInstructionSet instructionSet, bool isUpdateOperation)
        {
            this.InternalFile = internalFile;
            this.InstructionSet = instructionSet;
            this.IsUpdateOperation = isUpdateOperation;
            this.Thumbnails = new List<PackageThumbnailDescriptor>();
            this.Payloads = new List<PackagePayloadDescriptor>();
        }

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

        public byte[] Metadata { get; set; }

        /// <summary>
        /// Merges uploaded payloads and thumbnails
        /// </summary>
        public List<PayloadDescriptor> GetFinalPayloadDescriptors(bool fromManifest = false)
        {
            if (fromManifest)
            {
                return DescriptorsFromManifest();
            }

            return DescriptorsFromUploadedPayloads();
        }

        public List<PackagePayloadDescriptor> GetPayloadsWithValidIVs()
        {
            return Payloads.Where(p => p.HasIv()).ToList();
        }

        private List<PayloadDescriptor> DescriptorsFromUploadedPayloads()
        {
            var descriptors = Payloads.Select(p =>
            {
                var thumbnails = this.Thumbnails?.Where(t => t.PayloadKey == p.PayloadKey)
                    .Select(t => new ThumbnailDescriptor()
                    {
                        ContentType = t.ContentType,
                        PixelHeight = t.PixelHeight,
                        PixelWidth = t.PixelWidth,
                        BytesWritten = t.BytesWritten
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

        private List<PayloadDescriptor> DescriptorsFromManifest()
        {
            var descriptors = InstructionSet.Manifest.PayloadDescriptors.Select(p =>
            {
                var thumbnails = this.Thumbnails?.Where(t => t.PayloadKey == p.PayloadKey)
                    .Select(t => new ThumbnailDescriptor()
                    {
                        ContentType = t.ContentType,
                        PixelHeight = t.PixelHeight,
                        PixelWidth = t.PixelWidth,
                        BytesWritten = t.BytesWritten
                    }).ToList();

                return new PayloadDescriptor()
                {
                    Iv = p.Iv,
                    Uid = default,
                    Key = p.PayloadKey,
                    ContentType = p.ContentType,
                    Thumbnails = thumbnails,
                    LastModified = 0, //UnixTimeUtc.Now(),
                    BytesWritten = 0,
                    DescriptorContent = p.DescriptorContent,
                    PreviewThumbnail = p.PreviewThumbnail
                };
            });
            
            return descriptors.ToList();
        }
    }
}