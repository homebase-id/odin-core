using Youverse.Core.Services.Drive;
using Youverse.Core.Services.Transit.Upload;

namespace Youverse.Hosting.Tests
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
        public ExternalFileIdentifier UploadedFile { get; set; }

    }
}