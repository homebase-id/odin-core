namespace Odin.Core.Services.Drives.DriveCore.Storage;

/// <summary>
/// Defines a payload
/// </summary>
public class PayloadDescriptor
{
    /// <summary>
    /// A text value specified by the app to define the payload
    /// </summary>
    public string Key { get; set; }

    public uint BytesWritten { get; set; }
}