using System;
using System.Collections.Generic;
using System.Linq;
using Odin.Core.Time;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.FileSystem.Base.Upload;
using Odin.Services.Peer.Outgoing.Drive.Transfer;

namespace Odin.Services.Peer.Incoming.Drive.Transfer
{
    public class PeerPayloadPackage
    {
        /// <summary />
        public PeerPayloadPackage(InternalDriveFileId internalFile, PayloadTransferInstructionSet instructionSet)
        {
            this.TempFile = internalFile with { FileId = Guid.NewGuid() };
            this.InternalFile = internalFile;
            this.InstructionSet = instructionSet;
            this.Payloads = new List<PackagePayloadDescriptor>();
            this.Thumbnails = new List<PackageThumbnailDescriptor>();
        }

        public PayloadTransferInstructionSet InstructionSet { get; init; }

        /// <summary>
        /// The temporary file to which incoming payloads are written.  This is
        /// not the same as the target file to which the payloads will be attached
        /// </summary>
        public InternalDriveFileId TempFile { get; init; }

        /// <summary>
        /// The file to which the payloads will be attached
        /// </summary>
        public InternalDriveFileId InternalFile { get; init; }

        public List<PackagePayloadDescriptor> Payloads { get; init; }

        /// <summary>
        /// A list of thumbnails sent in the stream.
        /// this exists because payloads and thumbnails can
        /// be sent in any order (otherwise we would drop them
        /// in the payloads collection)
        /// </summary>
        public List<PackageThumbnailDescriptor> Thumbnails { get; init; }

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
                    Key = p.PayloadKey,
                    Uid = p.Uid,
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