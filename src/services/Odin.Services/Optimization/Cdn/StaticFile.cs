using System;
using System.Collections.Generic;
using Odin.Services.Apps;
using Odin.Services.Drives.DriveCore.Storage;

namespace Odin.Services.Optimization.Cdn;

public class StaticFile
{
    public SharedSecretEncryptedFileHeader Header { get; set; }

    public IEnumerable<ThumbnailContent> AdditionalThumbnails { get; set; }

    public List<PayloadStaticFileResponse> Payloads { get; set; }
}

public class PayloadStaticFileResponse
{
    /// <summary>
    /// A text value specified by the app to define the payload
    /// </summary>
    public string Key { get; set; }

    public string ContentType { get; set; }
    
    /// <summary>
    /// Base64 encoded byte array of the payload
    /// </summary>
    public string Data { get; set; }
}