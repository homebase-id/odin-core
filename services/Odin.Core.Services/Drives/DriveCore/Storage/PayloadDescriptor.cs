using Odin.Core.Time;

namespace Odin.Core.Services.Drives.DriveCore.Storage;

/// <summary>
/// Defines a payload
/// </summary>
public class PayloadDescriptor
{
    public PayloadDescriptor()
    {
        
    }
    /// <summary>
    /// A text value specified by the app to define the payload
    /// </summary>
    public string Key { get; set; }

    public string ContentType { get; set; }

    public long BytesWritten { get; set; }
    
    public UnixTimeUtc LastModified { get; set; }

    public bool IsValid()
    {
        var hasValidContentType = !(string.IsNullOrEmpty(ContentType) || string.IsNullOrWhiteSpace(ContentType));
        var hasValidKey = !(string.IsNullOrEmpty(Key) || string.IsNullOrWhiteSpace(Key));
        return hasValidKey && hasValidContentType;
    }
}

