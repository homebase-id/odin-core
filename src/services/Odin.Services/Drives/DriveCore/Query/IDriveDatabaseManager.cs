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
            QueryModifiedResultOptions options, IdentityDatabase db);


        /// <summary>
        /// Returns a batch of file Ids
        /// </summary>
        /// <returns>
        /// (resultFirstCursor, resultLastCursor, cursorUpdatedTimestamp, fileId List);
        /// </returns>
        Task<(QueryBatchCursor, IEnumerable<Guid>, bool hasMoreRows)> GetBatchCore(IOdinContext odinContext, FileSystemType fileSystemType, FileQueryParams qp,
            QueryBatchResultOptions options, IdentityDatabase db);

        /// <summary>
        /// Saves the file to the database
        /// </summary>
        Task SaveFileHeaderAsync(ServerFileHeader metadata, IdentityDatabase db);

        /// <summary>
        /// Todd says it aint soft and it aint hard ... mushy it is
        /// </summary>
        /// <returns></returns>
        Task SoftDeleteFileHeader(ServerFileHeader metadata, IdentityDatabase db);

        /// <summary>
        /// Removes the specified file from the index that is currently in use.
        /// </summary>
        Task HardDeleteFileHeaderAsync(InternalDriveFileId file, IdentityDatabase db);

        Task LoadLatestIndex(IdentityDatabase db);

        void AddReaction(OdinId odinId, Guid fileId, string reaction, IdentityDatabase db);

        void DeleteReactions(OdinId odinId, Guid fileId, IdentityDatabase db);

        void DeleteReaction(OdinId odinId, Guid fileId, string reaction, IdentityDatabase db);

        (List<string>, int) GetReactions(Guid fileId, IdentityDatabase db);

        (List<ReactionCount> reactions, int total) GetReactionSummaryByFile(Guid fileId, IdentityDatabase db);

        List<string> GetReactionsByIdentityAndFile(OdinId identity, Guid fileId, IdentityDatabase db);

        int GetReactionCountByIdentity(OdinId odinId, Guid fileId, IdentityDatabase db);

        (List<Reaction>, Int32? cursor) GetReactionsByFile(int maxCount, int cursor, Guid fileId, IdentityDatabase db);

        Task<(Int64 fileCount, Int64 byteSize)> GetDriveSizeInfo(IdentityDatabase db);

        Task<Guid?> GetByGlobalTransitId(Guid driveId, Guid globalTransitId, FileSystemType fileSystemType, IdentityDatabase db);

        Task<Guid?> GetByClientUniqueId(Guid driveId, Guid uniqueId, FileSystemType fileSystemType, IdentityDatabase db);

        Task<ServerFileHeader> GetFileHeaderAsync(Guid fileId, FileSystemType fileSystemType);
        
        Task SaveTransferHistoryAsync(Guid fileId, RecipientTransferHistory history, IdentityDatabase db);
        
        Task SaveReactionSummary(Guid fileId, ReactionSummary summary, IdentityDatabase db);
    }
}