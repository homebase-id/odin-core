using System;
using System.Collections.Generic;
using Dawn;
using Odin.Core.Services.Drives.DriveCore.Storage;

namespace Odin.Core.Services.Drives.FileSystem.Base.Upload.Attachments
{
    /// <summary>
    /// A package/parcel to be send to a set of recipients
    /// </summary>
    public class PayloadOnlyPackage
    {
        /// <summary />
        public PayloadOnlyPackage(InternalDriveFileId internalFile, UploadPayloadInstructionSet instructionSet)
        {
            Guard.Argument(internalFile, nameof(internalFile)).HasValue();
            Guard.Argument(internalFile.FileId, nameof(internalFile.FileId)).NotEqual(Guid.Empty);
            Guard.Argument(internalFile.DriveId, nameof(internalFile.DriveId)).NotEqual(Guid.Empty);

            this.InternalFile = internalFile;
            this.InstructionSet = instructionSet;
            this.UploadedPayloads = new List<PayloadDescriptor>();
        }

        public UploadPayloadInstructionSet InstructionSet { get; init; }

        public InternalDriveFileId InternalFile { get; init; }

        public List<PayloadDescriptor> UploadedPayloads { get; }
    }
}