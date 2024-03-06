using System;
using Odin.Services.Drives;

namespace Odin.Hosting.Controllers.Base.Drive;

public class DeletePayloadRequest
{
    public string Key { get; set; }

    public ExternalFileIdentifier File { get; set; }

    public Guid? VersionTag { get; set; }
}
