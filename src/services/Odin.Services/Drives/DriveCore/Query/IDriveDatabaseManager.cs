using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Odin.Core.Identity;
using Odin.Core.Storage;
using Odin.Core.Storage.SQLite;
using Odin.Core.Storage.SQLite.IdentityDatabase;
using Odin.Services.Base;
using Odin.Services.Drives.DriveCore.Query.Sqlite;
using Odin.Services.Drives.DriveCore.Storage;

namespace Odin.Services.Drives.DriveCore.Query
{
    /// <summary>
    /// Surfaces functions of the DriveDatabase for a specific drive
    /// </summary>
    public interface IDriveDatabaseManager : IDisposable
    {
        /// <summary>
        /// Specifies the drive being managed (indexed and queried)
        /// </summary>
        StorageDrive Drive { get; init; }

        /// <summary>
        /// Returns the fileId of recently modified files
        /// </summary>
        Task<(long, IEnumerable<Guid>, bool hasMoreRows)> GetModifiedCore(IOdinContext odinContext, FileSystemType fileSystemType, FileQueryParams qp,
            QueryModifiedResultOptions options, DatabaseConnection cn);


        /// <summary>
        /// Returns a batch of file Ids
        /// </summary>
        /// <returns>
        /// (resultFirstCursor, resultLastCursor, cursorUpdatedTimestamp, fileId List);
        /// </returns>
        Task<(QueryBatchCursor, IEnumerable<Guid>, bool hasMoreRows)> GetBatchCore(IOdinContext odinContext, FileSystemType fileSystemType, FileQueryParams qp,
            QueryBatchResultOptions options, DatabaseConnection cn);

        /// <summary>
        /// Updates the current index that is in use.
        /// </summary>
        Task UpdateCurrentIndex(ServerFileHeader metadata, DatabaseConnection cn);

        /// <summary>
        /// Todd says it aint soft and it aint hard ... mushy it is
        /// </summary>
        /// <param name="metadata"></param>
        /// <param name="cn"></param>
        /// <returns></returns>
        Task SoftDeleteFromIndex(ServerFileHeader metadata, DatabaseConnection cn);

        /// <summary>
        /// Removes the specified file from the index that is currently in use.
        /// </summary>
        /// <param name="file"></param>
        /// <returns></returns>
        Task HardDeleteFromIndex(InternalDriveFileId file, DatabaseConnection cn);

        Task LoadLatestIndex(DatabaseConnection cn);

        Task AddCommandMessage(List<Guid> fileIds, DatabaseConnection cn);

        Task<List<UnprocessedCommandMessage>> GetUnprocessedCommands(int count, DatabaseConnection cn);

        Task MarkCommandsCompleted(List<Guid> fileIds, DatabaseConnection cn);

        bool AddReaction(OdinId odinId, Guid fileId, string reaction, DatabaseConnection cn);

        bool DeleteReactions(OdinId odinId, Guid fileId, DatabaseConnection cn);

        bool DeleteReaction(OdinId odinId, Guid fileId, string reaction, DatabaseConnection cn);

        (List<string>, int) GetReactions(Guid fileId, DatabaseConnection cn);

        (List<ReactionCount> reactions, int total) GetReactionSummaryByFile(Guid fileId, DatabaseConnection cn);

        List<string> GetReactionsByIdentityAndFile(OdinId identity, Guid fileId, DatabaseConnection cn);

        int GetReactionCountByIdentity(OdinId odinId, Guid fileId, DatabaseConnection cn);

        (List<Reaction>, Int32? cursor) GetReactionsByFile(int maxCount, int cursor, Guid fileId, DatabaseConnection cn);

        Task<(Int64 fileCount, Int64 byteSize)> GetDriveSizeInfo(DatabaseConnection cn);

        Task<Guid?> GetByGlobalTransitId(Guid driveId, Guid globalTransitId, FileSystemType fileSystemType, DatabaseConnection cn);

        Task<Guid?> GetByClientUniqueId(Guid driveId, Guid uniqueId, FileSystemType fileSystemType, DatabaseConnection cn);
    }
}