using Odin.Services.Drives;

namespace Odin.Hosting.Controllers.Base.Drive;

public class SendReadReceiptRequest
{
    /// <summary>
    /// The file to be deleted
    /// </summary>
    public ExternalFileIdentifier File { get; set; }
}