using System.Collections.Generic;
using System.Linq;

namespace Odin.Services.Drives.DriveCore.Storage;

/// <summary>
/// Guards the "zombie payload" cleanups in <see cref="Odin.Services.Drives.FileSystem.Base.DriveStorageServiceBase"/>.
/// </summary>
public static class PayloadStorage
{
    /// <summary>
    /// Returns the subset of <paramref name="zombies"/> that is safe to hard-delete, i.e. everything that does
    /// not resolve to the same stored object as one of the payloads the file's header now points at.
    /// </summary>
    /// <remarks>
    /// A payload's storage path is <c>(fileId, Key, Uid)</c> and every zombie cleanup deletes under the *target*
    /// fileId, so a zombie sharing Key and Uid with a live payload is not a previous version of it: it IS it.
    /// Deleting it removes bytes the committed header still references, leaving a permanently unreadable file
    /// (reads return 404/NoSuchKey from the payload store).
    ///
    /// Uid changes on every payload upload, so equal Uids mean the same upload instance, which also means the
    /// same thumbnail set. Skipping the whole descriptor therefore strands nothing.
    ///
    /// The collision is normal, not exotic: the peer receive path preserves the sender's descriptor, so a
    /// retransmitted transfer (sender timed out and requeued while the first attempt was still in flight)
    /// delivers the same Key and Uid twice. The second delivery resolves to an overwrite of the first.
    /// </remarks>
    public static List<PayloadDescriptor> ExcludeStillLive(
        IEnumerable<PayloadDescriptor> zombies,
        IEnumerable<PayloadDescriptor> live)
    {
        var liveList = live?.ToList() ?? [];
        return (zombies ?? [])
            .Where(zombie => !liveList.Any(zombie.SharesStorageWith))
            .ToList();
    }
}
