using System;
using Youverse.Core.Cryptography;
using Youverse.Core.Services.Authentication;
using Youverse.Core.Services.Transit.Upload;
using Youverse.Hosting.Tests.AppAPI;

namespace Youverse.Hosting.Tests
{
    public class UploadTestUtilsContext
    {
        public Guid AppId { get; set; }
        public byte[] DeviceUid { get; set; }
        public ClientAuthToken AuthResult { get; set; }
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

        public TestSampleAppContext TestAppContext { get; set; }
    }
}