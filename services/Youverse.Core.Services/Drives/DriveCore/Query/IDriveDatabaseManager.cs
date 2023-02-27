using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Youverse.Core.Identity;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Drive;
using Youverse.Core.Services.Drive.Core.Query;
using Youverse.Core.Services.Drive.Core.Storage;
using Youverse.Core.Services.Drives.DriveCore.Query.Sqlite;
using Youverse.Core.Services.Drives.FileSystem;
using Youverse.Core.Storage;
using Youverse.Core.Storage.SQLite.DriveDatabase;

namespace Youverse.Core.Services.Drives.DriveCore.Query
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
        Task<(ulong, IEnumerable<Guid>)> GetModified(DotYouContext dotYouContext, FileSystemType fileSystemType, FileQueryParams qp, QueryModifiedResultOptions options);


        /// <summary>
        /// Returns a batch of file Ids
        /// </summary>
        /// <returns>
        /// (resultFirstCursor, resultLastCursor, cursorUpdatedTimestamp, fileId List);
        /// </returns>
        Task<(QueryBatchCursor, IEnumerable<Guid>)> GetBatch(DotYouContext dotYouContext, FileSystemType fileSystemType, FileQueryParams qp, QueryBatchResultOptions options);

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

        void EnsureIndexDataCommitted();

        void AddReaction(OdinId dotYouId, Guid fileId, string reaction);

        void DeleteReactions(OdinId dotYouId, Guid fileId);

        void DeleteReaction(OdinId dotYouId, Guid fileId, string reaction);

        (List<string>, int) GetReactions(Guid fileId);

        int GetReactionCountByIdentity(OdinId odinId, Guid fileId);

        (List<Reaction>, Int32? cursor) GetReactionsByFile(int maxCount, int cursor, Guid fileId);
    }
}