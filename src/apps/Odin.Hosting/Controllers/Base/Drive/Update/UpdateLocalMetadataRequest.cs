using System;
using System.Collections.Generic;
using Odin.Services.Drives;

namespace Odin.Hosting.Controllers.Base.Drive.Update;

public class UpdateLocalMetadataRequest
{
    public ExternalFileIdentifier File { get; set; }

    public string Content { get; set; }

    public List<Guid> Tags { get; set; }
}