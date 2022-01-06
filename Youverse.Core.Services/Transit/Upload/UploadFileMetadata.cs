using Youverse.Core.Services.Drive.Storage;

namespace Youverse.Core.Services.Transit.Upload
{
    public class UploadFileMetadata
    {
        public UploadFileMetadata()
        {
            this.AppData = new();
        }

        public string ContentType { get; set; }

        public UploadAppFileMetaData AppData { get; set; }
    }
}