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
        Task<(long, IEnumerable<Guid>, bool hasMoreRows)> GetModifiedCoreAsync(IOdinContext odinContext, FileSystemType fileSystemType, FileQueryParams qp,
            QueryModifiedResultOptions options, IdentityDatabase db);


        /// <summary>
        /// Returns a batch of file Ids
        /// </summary>
        /// <returns>
        /// (resultFirstCursor, resultLastCursor, cursorUpdatedTimestamp, fileId List);
        /// </returns>
        Task<(QueryBatchCursor, IEnumerable<Guid>, bool hasMoreRows)> GetBatchCoreAsync(IOdinContext odinContext, FileSystemType fileSystemType, FileQueryParams qp,
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

        Task LoadLatestIndexAsync(IdentityDatabase db);

        Task AddReactionAsync(OdinId odinId, Guid fileId, string reaction, IdentityDatabase db);

        Task DeleteReactionsAsync(OdinId odinId, Guid fileId, IdentityDatabase db);

        Task DeleteReactionAsync(OdinId odinId, Guid fileId, string reaction, IdentityDatabase db);

        Task<(List<string>, int)> GetReactionsAsync(Guid fileId, IdentityDatabase db);

        Task<(List<ReactionCount> reactions, int total)> GetReactionSummaryByFileAsync(Guid fileId, IdentityDatabase db);

        Task<List<string>> GetReactionsByIdentityAndFileAsync(OdinId identity, Guid fileId, IdentityDatabase db);

        Task<int> GetReactionCountByIdentityAsync(OdinId odinId, Guid fileId, IdentityDatabase db);

        Task<(List<Reaction>, Int32? cursor)> GetReactionsByFileAsync(int maxCount, int cursor, Guid fileId, IdentityDatabase db);

        Task<(Int64 fileCount, Int64 byteSize)> GetDriveSizeInfoAsync(IdentityDatabase db);

        Task<Guid?> GetByGlobalTransitIdAsync(Guid driveId, Guid globalTransitId, FileSystemType fileSystemType, IdentityDatabase db);

        Task<Guid?> GetByClientUniqueIdAsync(Guid driveId, Guid uniqueId, FileSystemType fileSystemType, IdentityDatabase db);

        Task<ServerFileHeader> GetFileHeaderAsync(Guid fileId, FileSystemType fileSystemType);
        
        Task SaveTransferHistoryAsync(Guid fileId, RecipientTransferHistory history, IdentityDatabase db);
        
        Task SaveReactionSummary(Guid fileId, ReactionSummary summary, IdentityDatabase db);
    }
}