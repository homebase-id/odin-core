namespace Odin.Services.Drives.DriveCore.Storage;

/// Identifies where a not-yet-committed file's payloads live, carrying the backend
/// implicitly (resolved to a concrete IDriveFileStore in DriveStorageServiceBase).
public enum StagingArea
{
    /// Owner/app upload: payloads staged under the per-drive temp/uploads folder, copied to long-term at commit.
    Upload,

    // TODO:INBOX Delete this whole folder-based staging mode once the inbox folder is drained (no pre-upgrade
    // items left in any inbox). Both producers now carry metadata on the inbox row, so nothing new is written
    // to the folder. Grep StagingArea.Inbox for every consumer that goes with it.
    /// Legacy peer-inbox staging: payloads staged under the per-drive inbox folder, copied to long-term at commit.
    Inbox,

    /// Peer inbox, no folder: the receive path streamed the payloads straight to long-term storage (under the
    /// incoming fileId), so there is nothing to copy at commit time. For a brand-new file the incoming fileId
    /// IS the final fileId and the bytes are already in place; for an overwrite/update the payloads are relocated
    /// within long-term from the incoming fileId to the target fileId. See
    /// DriveStorageServiceBase.CopyPayloadAndThumbnailsToLongTermStorage.
    LongTerm
}
