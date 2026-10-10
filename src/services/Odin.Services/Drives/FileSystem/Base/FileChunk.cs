using System;

namespace Odin.Services.Drives.FileSystem.Base;

/// <summary>
/// A byte range of a payload. A null <see cref="Length"/> means "to the end".
/// </summary>
public class FileChunk
{
    public Int64 Start { get; set; }
    public Int64? Length { get; set; }
}
