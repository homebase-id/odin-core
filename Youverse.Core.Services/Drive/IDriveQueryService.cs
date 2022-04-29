using System;
using System.Threading.Tasks;

namespace Youverse.Core.Services.Drive
{
    /// <summary>
    /// Access to configured drives, their files
    /// </summary>
    public interface IDriveQueryService
    {
        Task<PagedResult<DriveSearchResult>> GetRecentlyCreatedItems(Guid driveId, bool includeMetadataHeader, bool includePayload, PageOptions pageOptions);
        
        Task<PagedResult<DriveSearchResult>> GetByFileType(Guid driveId, int fileType, bool includeMetadataHeader, bool includePayload, PageOptions pageOptions);
        
        Task<PagedResult<DriveSearchResult>> GetByTag(Guid driveId, Guid tag, int fileType, bool includeMetadataHeader, bool includePayload, PageOptions pageOptions);

        Task<PagedResult<DriveSearchResult>> GetByAlias(Guid driveId, Guid alias, bool includeMetadataHeader, bool includePayload, PageOptions pageOptions);

        Task RebuildBackupIndex(Guid driveId);

        Task RebuildAllIndices();
    }
}