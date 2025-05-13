using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Odin.Core.Identity;
using Odin.Core.Storage;
using Odin.Core.Storage.Database.Identity.Table;
using Odin.Services.Base;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Peer.Incoming.Drive.Transfer;

namespace Odin.Services.Drives.DriveCore.Query
{
    /// <summary>
    /// Surfaces functions of the DriveDatabase for a specific drive
    /// </summary>
    public interface IDriveDatabaseManager
    {
        /// <summary>
        /// Returns the fileId of recently modified files
        /// </summary>
        Task<(string, List<DriveMainIndexRecord>, bool hasMoreRows)> GetModifiedCoreAsync(
            StorageDrive drive,
            IOdinContext odinContext,
            FileSystemType fileSystemType,
            FileQueryParams qp,
            QueryModifiedResultOptions options);

        /// <summary>
        /// Returns a batch of file Ids
        /// </summary>
        /// <returns>
        /// (resultFirstCursor, resultLastCursor, cursorUpdatedTimestamp, fileId List);
        /// </returns>
        Task<(QueryBatchCursor, List<DriveMainIndexRecord>, bool hasMoreRows)> GetBatchCoreAsync(
            StorageDrive drive,
            IOdinContext odinContext,
            FileSystemType fileSystemType,
            FileQueryParams qp,
            QueryBatchResultOptions options);

        /// <summary>
        /// Saves the file to the database
        /// </summary>
        Task SaveFileHeaderAsync(StorageDrive drive, ServerFileHeader metadata, Guid? useThisVersionTag);

        /// <summary>
        /// Todd says it aint soft and it aint hard ... mushy it is
        /// </summary>
        /// <returns></returns>
        Task SoftDeleteFileHeader(ServerFileHeader metadata);

        /// <summary>
        /// Removes the specified file from the index that is currently in use.
        /// </summary>
        Task HardDeleteFileHeaderAsync(StorageDrive drive, InternalDriveFileId file);

        Task AddReactionAsync(StorageDrive drive, OdinId odinId, Guid fileId, string reaction, WriteSecondDatabaseRowBase markComplete);

        Task DeleteReactionsAsync(StorageDrive drive, OdinId odinId, Guid fileId);

        Task DeleteReactionAsync(StorageDrive drive, OdinId odinId, Guid fileId, string reaction);

        Task<(List<string>, int)> GetReactionsAsync(StorageDrive drive, Guid fileId);

        Task<(List<ReactionCount> reactions, int total)> GetReactionSummaryByFileAsync(StorageDrive drive, Guid fileId);

        Task<List<string>> GetReactionsByIdentityAndFileAsync(StorageDrive drive, OdinId identity, Guid fileId);

        Task<int> GetReactionCountByIdentityAsync(StorageDrive drive, OdinId odinId, Guid fileId);

        Task<(List<Reaction>, Int32? cursor)> GetReactionsByFileAsync(StorageDrive drive, int maxCount, int cursor, Guid fileId);

        Task<(Int64 fileCount, Int64 byteSize)> GetDriveSizeInfoAsync(StorageDrive drive);

        Task<DriveMainIndexRecord> GetByGlobalTransitIdAsync(Guid driveId, Guid globalTransitId, FileSystemType fileSystemType);

        Task<DriveMainIndexRecord> GetByClientUniqueIdAsync(Guid driveId, Guid uniqueId, FileSystemType fileSystemType);

        Task<ServerFileHeader> GetFileHeaderAsync(StorageDrive drive, Guid fileId, FileSystemType fileSystemType);

        Task SaveReactionSummary(StorageDrive drive, Guid fileId, ReactionSummary summary);
    }
}