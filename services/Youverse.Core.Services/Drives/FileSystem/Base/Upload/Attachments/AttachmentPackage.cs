using System;
using System.Collections.Generic;
using Dawn;
using Youverse.Core.Services.Drives.DriveCore.Storage;

namespace Youverse.Core.Services.Drives.FileSystem.Base.Upload.Attachments
{
    /// <summary>
    /// A package/parcel to be send to a set of recipients
    /// </summary>
    public class AttachmentPackage
    {
        /// <summary />
        public AttachmentPackage(InternalDriveFileId internalFile, AddAttachmentInstructionSet instructionSet)
        {
            Guard.Argument(internalFile, nameof(internalFile)).HasValue();
            Guard.Argument(internalFile.FileId, nameof(internalFile.FileId)).NotEqual(Guid.Empty);
            Guard.Argument(internalFile.DriveId, nameof(internalFile.DriveId)).NotEqual(Guid.Empty);

            this.InternalFile = internalFile;
            this.InstructionSet = instructionSet;

        }

        public AddAttachmentInstructionSet InstructionSet { get; init; }
        

        public InternalDriveFileId InternalFile { get; init; }
        
        public bool HasPayload { get; set; }
    }
}