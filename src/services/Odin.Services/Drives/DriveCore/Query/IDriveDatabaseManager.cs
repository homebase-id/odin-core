using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Odin.Core.Identity;
using Odin.Core.Storage;
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
        Task<(long, IEnumerable<Guid>, bool hasMoreRows)> GetModifiedCore(OdinContext odinContext, FileSystemType fileSystemType, FileQueryParams qp,
            QueryModifiedResultOptions options);


        /// <summary>
        /// Returns a batch of file Ids
        /// </summary>
        /// <returns>
        /// (resultFirstCursor, resultLastCursor, cursorUpdatedTimestamp, fileId List);
        /// </returns>
        Task<(QueryBatchCursor, IEnumerable<Guid>, bool hasMoreRows)> GetBatchCore(OdinContext odinContext, FileSystemType fileSystemType, FileQueryParams qp,
            QueryBatchResultOptions options);

        /// <summary>
        /// Updates the current index that is in use.
        /// </summary>
        Task UpdateCurrentIndex(ServerFileHeader metadata);

        /// <summary>
        /// Removes the specified file from the index that is currently in use.
        /// </summary>
        /// <param name="file"></param>
        /// <returns></returns>
        Task RemoveFromCurrentIndex(InternalDriveFileId file);

        Task LoadLatestIndex();

        Task AddCommandMessage(List<Guid> fileIds);

        Task<List<UnprocessedCommandMessage>> GetUnprocessedCommands(int count);

        Task MarkCommandsCompleted(List<Guid> fileIds);

        void AddReaction(OdinId odinId, Guid fileId, string reaction);

        void DeleteReactions(OdinId odinId, Guid fileId);

        void DeleteReaction(OdinId odinId, Guid fileId, string reaction);

        (List<string>, int) GetReactions(Guid fileId);

        (List<ReactionCount> reactions, int total) GetReactionSummaryByFile(Guid fileId);

        List<string> GetReactionsByIdentityAndFile(OdinId identity, Guid fileId);

        int GetReactionCountByIdentity(OdinId odinId, Guid fileId);

        (List<Reaction>, Int32? cursor) GetReactionsByFile(int maxCount, int cursor, Guid fileId);
    }
}