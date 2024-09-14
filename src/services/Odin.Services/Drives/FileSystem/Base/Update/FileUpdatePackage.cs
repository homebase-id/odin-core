using System;
using System.Collections.Generic;
using System.Linq;
using Odin.Core;
using Odin.Core.Storage;
using Odin.Core.Time;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.FileSystem.Base.Upload;

namespace Odin.Services.Drives.FileSystem.Base.Update
{
    public class FileUpdatePackage
    {
        public FileSystemType FileSystemType { get; }

        /// <summary />
        public FileUpdatePackage(InternalDriveFileId internalFile, FileUpdateInstructionSet instructionSet, FileSystemType fileSystemType)
        {
            this.TempMetadataFile = new InternalDriveFileId()
            {
                FileId = SequentialGuid.CreateGuid(UnixTimeUtc.Now()),
                DriveId = internalFile.DriveId
            };
            
            this.Thumbnails = new List<PackageThumbnailDescriptor>();
            this.Payloads = new List<PackagePayloadDescriptor>();
            FileSystemType = fileSystemType;
            this.TargetVersionTag = SequentialGuid.CreateGuid();
        }

        /// <summary>
        /// A temp file name for use while storing the temporary metadata file being uploaded
        /// This is not the same as the final target file.
        /// </summary>
        public InternalDriveFileId TempMetadataFile { get; init; }

        public FileUpdateInstructionSet InstructionSet { get; init; }

        public InternalDriveFileId InternalFile { get; init; }
        
        public Guid TargetVersionTag { get; init; }
        
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

        public List<PackagePayloadDescriptor> GetPayloadsWithValidIVs()
        {
            return Payloads.Where(p => p.HasIv()).ToList();
        }
    }
}