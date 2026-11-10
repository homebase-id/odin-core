using Odin.Core.Identity;
using Odin.Services.Drives;

namespace Odin.Services.Base;

/// <summary>
/// A file changed for its original sender, <paramref name="Sender"/>, who may change a file they sent without reading it
/// (docs/file-access.md, "Changing a file you sent").
/// </summary>
public record SendersOwnFile(InternalDriveFileId File, OdinId Sender)
{
    /// <summary>The sender <paramref name="odinContext"/> is marked to change <paramref name="file"/> for; null when it isn't.</summary>
    public static OdinId? SenderOf(IOdinContext odinContext, InternalDriveFileId file) =>
        odinContext.SendersOwnFile is { } own && own.File == file ? own.Sender : null;
}
