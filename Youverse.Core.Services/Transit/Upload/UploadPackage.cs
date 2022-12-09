using System;
using System.IO;
using System.Runtime.CompilerServices;
using Dawn;
using Youverse.Core.Services.Drive;

namespace Youverse.Core.Services.Transit.Upload
{
    /// <summary>
    /// A package/parcel to be send to a set of recipients
    /// </summary>
    public class UploadPackage
    {
        public UploadPackage(Guid id, InternalDriveFileId internalFile, UploadInstructionSet instructionSet, bool isUpdateOperation)
        {
            Guard.Argument(internalFile, nameof(internalFile)).HasValue();
            Guard.Argument(internalFile.FileId, nameof(internalFile.FileId)).NotEqual(Guid.Empty);
            Guard.Argument(internalFile.DriveId, nameof(internalFile.DriveId)).NotEqual(Guid.Empty);

            this.Id = id;
            this.InternalFile = internalFile;
            this.InstructionSet = instructionSet;
            this.IsUpdateOperation = isUpdateOperation;
        }

        public Guid Id { get; init; }
        
        public UploadInstructionSet InstructionSet { get; init; }

        public InternalDriveFileId InternalFile { get; init; }

        public bool IsUpdateOperation { get; init; }
        
        public bool HasPayload { get; set; }
    }
}