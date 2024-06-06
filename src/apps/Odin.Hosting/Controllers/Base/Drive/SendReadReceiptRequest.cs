using System.Collections.Generic;
using Odin.Services.Drives;

namespace Odin.Hosting.Controllers.Base.Drive;

public class SendReadReceiptRequest
{
    public List<ExternalFileIdentifier> Files { get; set; }
}