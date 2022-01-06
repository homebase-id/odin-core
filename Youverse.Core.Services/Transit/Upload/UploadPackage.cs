using System;
using Dawn;
using Youverse.Core.Services.Drive;

namespace Youverse.Core.Services.Transit.Upload
{
    /// <summary>
    /// A package/parcel to be send to a set of recipients
    /// </summary>
    public class UploadPackage
    {
        public UploadPackage(DriveFileId file, UploadInstructionSet instructionSet, int expectedPartsCount = 3)
        {
            Guard.Argument(file, nameof(file)).HasValue();
            Guard.Argument(file.FileId, nameof(file.FileId)).NotEqual(Guid.Empty);
            Guard.Argument(file.DriveId, nameof(file.DriveId)).NotEqual(Guid.Empty);

            this.File = file;
            this.InstructionSet = instructionSet;
            this.ExpectedPartsCount = expectedPartsCount;
        }

        public UploadInstructionSet InstructionSet { get; set; }
        
        public DriveFileId File { get; set; }
        public int ExpectedPartsCount { get; }
    }
}