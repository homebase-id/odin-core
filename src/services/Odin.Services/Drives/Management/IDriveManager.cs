using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Odin.Core;
using Odin.Services.Base;

namespace Odin.Services.Drives.Management;

public interface IDriveManager
{
    Task<StorageDrive> CreateDriveAsync(CreateDriveRequest request, IOdinContext odinContext);
    Task SetDriveReadModeAsync(Guid driveId, bool allowAnonymous, IOdinContext odinContext);
    Task SetDriveAllowSubscriptionsAsync(Guid driveId, bool allowSubscriptions, IOdinContext odinContext);
    Task UpdateMetadataAsync(Guid driveId, string metadata, IOdinContext odinContext);
    Task UpdateAttributesAsync(Guid driveId, Dictionary<string, string> attributes, IOdinContext odinContext);
    Task<StorageDrive> GetDriveAsync(Guid driveId, bool failIfInvalid = false);
    Task<PagedResult<StorageDrive>> GetDrivesAsync(PageOptions pageOptions, IOdinContext odinContext);
    Task<PagedResult<StorageDrive>> GetDrivesAsync(GuidId type, PageOptions pageOptions, IOdinContext odinContext);
    Task<PagedResult<StorageDrive>> GetAnonymousDrivesAsync(PageOptions pageOptions, IOdinContext odinContext);
    Task<PagedResult<StorageDrive>> GetCdnEnabledDrivesAsync(PageOptions pageOptions, IOdinContext odinContext);
}