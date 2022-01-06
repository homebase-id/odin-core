using Youverse.Core.Services.Drive.Storage;

namespace Youverse.Core.Services.Transit.Upload
{
    public class UploadFileMetadata
    {
        public UploadFileMetadata()
        {
            this.AppData = new AppFileMetaData();
        }

        public string ContentType { get; set; }

        public AppFileMetaData AppData { get; set; }
    }
}