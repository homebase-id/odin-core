using System;
using Youverse.Core.Cryptography;
using Youverse.Core.Services.Authentication;
using Youverse.Core.Services.Transit.Upload;

namespace Youverse.Hosting.Tests
{
    public class UploadTestUtilsContext
    {
        public Guid AppId { get; set; }
        public byte[] DeviceUid { get; set; }
        public DotYouAuthenticationResult AuthResult { get; set; }
        public SensitiveByteArray AppSharedSecretKey { get; set; }

        /// <summary>
        /// The instruction set that was uploaded
        /// </summary>
        public UploadInstructionSet InstructionSet { get; set; }

        /// <summary>
        /// The file meta data that was uploaded. 
        /// </summary>
        public UploadFileMetadata FileMetadata { get; set; }
        
        public string PayloadData { get; set; }

    }
}