using System;
using System.Collections.Generic;
using Dawn;
using Odin.Core.Services.Drives.DriveCore.Storage;

namespace Odin.Core.Services.Drives.FileSystem.Base.Upload
{
    /// <summary>
    /// A package/parcel to be send to a set of recipients
    /// </summary>
    public class UploadPackage
    {
        /// <summary />
        public UploadPackage(InternalDriveFileId internalFile, UploadInstructionSet instructionSet, bool isUpdateOperation)
        {
            Guard.Argument(internalFile, nameof(internalFile)).HasValue();
            Guard.Argument(internalFile.FileId, nameof(internalFile.FileId)).NotEqual(Guid.Empty);
            Guard.Argument(internalFile.DriveId, nameof(internalFile.DriveId)).NotEqual(Guid.Empty);

            this.InternalFile = internalFile;
            this.InstructionSet = instructionSet;
            this.IsUpdateOperation = isUpdateOperation;
            this.UploadedThumbnails = new List<ImageDataHeader>();
        }


        public UploadInstructionSet InstructionSet { get; init; }

        public InternalDriveFileId InternalFile { get; init; }

        public bool IsUpdateOperation { get; init; }
        
        public bool HasPayload { get; set; }

        /// <summary>
        /// A lit of thumbnails sent in the stream
        /// </summary>
        public List<ImageDataHeader> UploadedThumbnails { get; }

    }
}