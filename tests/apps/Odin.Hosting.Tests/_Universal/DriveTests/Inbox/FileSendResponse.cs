using Odin.Services.Drives.FileSystem.Base.Upload;

namespace Odin.Hosting.Tests._Universal.DriveTests.Inbox;

internal class FileSendResponse
{
    public UploadResult UploadResult { get; set; }
    public string DecryptedContent { get; set; }
    public string EncryptedContent64 { get; set; }
}