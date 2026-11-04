using Odin.Core.Identity;
using Odin.Services.Drives;

namespace Odin.Services.Base;

/// <summary>
/// A file changed for its original sender, <paramref name="Sender"/>, who may change a file they sent without reading it
/// (docs/file-access.md, "Changing a file you sent").
/// </summary>
public record SendersOwnFile(InternalDriveFileId File, OdinId Sender);
