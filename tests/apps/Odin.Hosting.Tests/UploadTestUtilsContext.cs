using Odin.Services.Drives;
using Odin.Services.Drives.FileSystem.Base.Upload;
using Odin.Core.Storage;

namespace Odin.Hosting.Tests
{
    public class UploadTestUtilsContext
    {
        /// <summary>
        /// The instruction set that was uploaded
        /// </summary>
        public UploadInstructionSet InstructionSet { get; set; }

        /// <summary>
        /// The file meta data that was uploaded. 
        /// </summary>
        public UploadFileMetadata UploadFileMetadata { get; set; }

        /// <summary>
        /// The payload data that was uploaded
        /// </summary>
        public string PayloadData { get; set; }

        /// <summary>
        /// The uploaded file information.
        /// </summary>
        public ExternalFileIdentifier UploadedFile => this.UploadResult.File;

        public byte[] PayloadCipher { get; set; }
        public FileSystemType FileSystemType { get; set; }
        public UploadResult UploadResult { get; set; }
    }
}