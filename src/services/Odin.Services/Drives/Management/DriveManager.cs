using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using MediatR;
using Microsoft.Extensions.Logging;
using Odin.Core;
using Odin.Core.Cryptography.Crypto;
using Odin.Core.Cryptography.Data;
using Odin.Core.Exceptions;
using Odin.Core.Serialization;
using Odin.Core.Storage.Cache;
using Odin.Core.Storage.Database.Identity.Table;
using Odin.Services.Authorization.Acl;
using Odin.Services.Base;
using Odin.Services.Mediator;
using Odin.Services.Util;

[assembly: InternalsVisibleTo("Odin.Hosting")]

namespace Odin.Services.Drives.Management;

#nullable enable

/// <summary>
/// Manages drive creation, metadata updates, and their overall definitions
/// </summary>
public class DriveManager : IDriveManager
{
    private const string CacheKeyDrive = "drive:";
    private const string CacheKeyAllDrives = "alldrives";
    private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(2);
    private static readonly List<string> RootInvalidationTag = [TableDrivesCached.RootInvalidationTag];

    // IMPORTANT: We're not using the generic variant of ITenantLevel2Cache<> here.
    // This is so "invalidation tags" are in the same "namespace" as the ones in TableDrivesCached.
    private readonly ITenantLevel2Cache _driveCache;

    private readonly ILogger<DriveManager> _logger;
    private readonly IMediator _mediator;
    private readonly TenantContext _tenantContext;
    private readonly TableDrivesCached _tableDrives;

    /// <summary>
    /// Manages drive creation, metadata updates, and their overall definitions
    /// </summary>
    public DriveManager(
        ILogger<DriveManager> logger,
        ITenantLevel2Cache driveCache,
        IMediator mediator,
        TenantContext tenantContext,
        TableDrivesCached tableDrives)
    {
        _logger = logger;
        _driveCache = driveCache;
        _mediator = mediator;
        _tenantContext = tenantContext;
        _tableDrives = tableDrives;
    }

    public async Task<StorageDrive> CreateDriveAsync(CreateDriveRequest request, IOdinContext odinContext)
    {
        if (string.IsNullOrEmpty(request.Name))
        {
            throw new OdinClientException("Name cannot be empty");
        }

        OdinValidationUtils.AssertIsValidTargetDriveValue(request.TargetDrive);

        if (request.OwnerOnly && request.AllowAnonymousReads)
        {
            throw new OdinClientException("A drive cannot be owner-only and allow anonymous reads",
                OdinClientErrorCode.CannotAllowAnonymousReadsOnOwnerOnlyDrive);
        }

        if (request.OwnerOnly && request.AllowSubscriptions)
        {
            throw new OdinClientException("A drive cannot be owner-only and allow subscriptions",
                OdinClientErrorCode.CannotAllowSubscriptionsOnOwnerOnlyDrive);
        }

        var existingDriveByTargetDriveAsync = await _tableDrives.GetByTargetDriveAsync(request.TargetDrive.Alias, request.TargetDrive.Type);
        if (null != existingDriveByTargetDriveAsync)
        {
            throw new OdinClientException("Drive by alias and type already exists", OdinClientErrorCode.InvalidDrive);
        }

        var mk = odinContext.Caller.GetMasterKey();

        var driveKey = new SymmetricKeyEncryptedAes(mk);

        var id = request.TargetDrive.Alias.Value;
        var storageKey = driveKey.DecryptKeyClone(mk);

        (byte[] encryptedIdIv, byte[] encryptedIdValue) = AesCbc.Encrypt(id.ToByteArray(), storageKey);

        var driveData = new StorageDriveDetails()
        {
            TargetDriveInfo = request.TargetDrive,
            Metadata = request.Metadata,
            AllowAnonymousReads = request.AllowAnonymousReads,
            AllowSubscriptions = request.AllowSubscriptions,
            OwnerOnly = request.OwnerOnly,
            Attributes = request.Attributes
        };

        var record = new DrivesRecord
        {
            DriveId = id,
            DriveName = request.Name,
            DriveType = request.TargetDrive.Type.Value,
            MasterKeyEncryptedStorageKeyJson = OdinSystemSerializer.Serialize(driveKey),
            EncryptedIdIv64 = encryptedIdIv.ToBase64(),
            EncryptedIdValue64 = encryptedIdValue.ToBase64(),
            detailsJson = OdinSystemSerializer.Serialize(driveData),
            StorageKeyCheckValue = id
        };

        try
        {
            if (!await _tableDrives.TryInsertAsync(record))
            {
                throw new OdinClientException("Existing drive", OdinClientErrorCode.InvalidDrive);
            }
        }
        finally
        {
            storageKey.Wipe();
        }

        var storageDrive = ToStorageDrive(ToStorageDriveData(record));
        storageDrive.CreateDirectories();

        _logger.LogDebug("Created a new Drive - {drive}", storageDrive.TargetDriveInfo);

        await _mediator.Publish(new DriveDefinitionAddedNotification
        {
            IsNewDrive = true,
            Drive = storageDrive,
            OdinContext = odinContext,
        });

        return storageDrive;
    }

    public async Task SetDriveReadModeAsync(Guid driveId, bool allowAnonymous, IOdinContext odinContext)
    {
        odinContext.Caller.AssertHasMasterKey();

        var storageDrive = await GetDriveAsync(driveId);
        if (storageDrive == null)
        {
            throw new OdinClientException($"Invalid drive id {driveId}", OdinClientErrorCode.InvalidDrive);
        }

        if (SystemDriveConstants.SystemDrives.Any(d => d == storageDrive.TargetDriveInfo))
        {
            throw new OdinSecurityException("Cannot change system drive");
        }

        if (storageDrive.OwnerOnly && allowAnonymous)
        {
            throw new OdinSecurityException("Cannot set Owner Only drive to allow anonymous");
        }

        //only change if needed
        if (storageDrive.AllowAnonymousReads != allowAnonymous)
        {
            storageDrive.AllowAnonymousReads = allowAnonymous;

            await _tableDrives.UpsertAsync(ToRecord(storageDrive.Data));

            await _mediator.Publish(new DriveDefinitionAddedNotification
            {
                IsNewDrive = false,
                Drive = storageDrive,
                OdinContext = odinContext,
            });
        }
    }

    public async Task SetDriveAllowSubscriptionsAsync(Guid driveId, bool allowSubscriptions, IOdinContext odinContext)
    {
        odinContext.Caller.AssertHasMasterKey();

        var storageDrive = await GetDriveAsync(driveId);
        if (storageDrive == null)
        {
            throw new OdinClientException($"Invalid drive id {driveId}", OdinClientErrorCode.InvalidDrive);
        }

        if (SystemDriveConstants.SystemDrives.Any(d => d == storageDrive.TargetDriveInfo))
        {
            throw new OdinSecurityException("Cannot change system drive");
        }

        if (storageDrive.OwnerOnly && allowSubscriptions)
        {
            throw new OdinSecurityException("Cannot set Owner Only drive to allow anonymous");
        }

        //only change if needed
        if (storageDrive.AllowSubscriptions != allowSubscriptions)
        {
            storageDrive.AllowSubscriptions = allowSubscriptions;

            await _tableDrives.UpsertAsync(ToRecord(storageDrive.Data));

            await _mediator.Publish(new DriveDefinitionAddedNotification
            {
                IsNewDrive = false,
                Drive = storageDrive,
                OdinContext = odinContext
            });
        }
    }

    public async Task SetArchiveDriveFlagAsync(Guid driveId, bool value, IOdinContext odinContext)
    {
        odinContext.Caller.AssertHasMasterKey();

        var storageDrive = await GetDriveAsync(driveId);

        if (storageDrive == null)
        {
            throw new OdinClientException($"Invalid drive id {driveId}", OdinClientErrorCode.InvalidDrive);
        }

        if (SystemDriveConstants.SystemDrives.Any(d => d == storageDrive.TargetDriveInfo))
        {
            throw new OdinClientException("Cannot archive system drive");
        }

        //only change if needed
        if (storageDrive.IsArchived != value)
        {
            _logger.LogDebug("Archiving Drive - new value: {e}", value);
            storageDrive.IsArchived = value;

            var affected = await _tableDrives.UpsertAsync(ToRecord(storageDrive.Data));
            _logger.LogDebug("Archiving Drive - rows affected value: {e}", affected);
            if (affected != 1)
            {
                throw new OdinSystemException($"Archive drive should have updated 1 and only 1 row.  Number updated: {affected}");
            }

            await _mediator.Publish(new DriveDefinitionAddedNotification
            {
                IsNewDrive = false,
                Drive = storageDrive,
                OdinContext = odinContext,
            });
        }
    }

    public async Task UpdateMetadataAsync(Guid driveId, string metadata, IOdinContext odinContext)
    {
        odinContext.Caller.AssertHasMasterKey();

        var storageDrive = await GetDriveInternal(driveId);
        if (storageDrive == null)
        {
            throw new OdinClientException($"Invalid drive id {driveId}", OdinClientErrorCode.InvalidDrive);
        }

        storageDrive.Metadata = metadata;
        await _tableDrives.UpsertAsync(ToRecord(storageDrive));
    }

    public async Task UpdateAttributesAsync(Guid driveId, Dictionary<string, string> attributes, IOdinContext odinContext)
    {
        odinContext.Caller.AssertHasMasterKey();

        var storageDrive = await GetDriveInternal(driveId);
        if (storageDrive == null)
        {
            throw new OdinClientException($"Invalid drive id {driveId}", OdinClientErrorCode.InvalidDrive);
        }

        storageDrive.Attributes = attributes;

        await _tableDrives.UpsertAsync(ToRecord(storageDrive));
    }

    public async Task<StorageDrive?> GetDriveAsync(Guid driveId, bool failIfInvalid = false)
    {
        var driveData = await _driveCache.GetOrSetAsync(
            CacheKeyDrive + driveId,
            _ => GetDriveInternal(driveId),
            CacheTtl,
            RootInvalidationTag);

        if (driveData == null)
        {
            if (failIfInvalid)
            {
                throw new OdinClientException($"Invalid drive id {driveId}", OdinClientErrorCode.InvalidDrive);
            }

            return null;
        }

        return ToStorageDrive(driveData);
    }

    public async Task<PagedResult<StorageDrive>> GetDrivesAsync(PageOptions pageOptions, IOdinContext odinContext)
    {
        Func<StorageDrive, bool> predicate = _ => true;
        if (odinContext.Caller.IsAnonymous)
        {
            predicate = drive => drive.AllowAnonymousReads && drive.OwnerOnly == false;
        }

        var page = await GetDrivesInternalAsync(false, pageOptions, odinContext);
        var storageDrives = page.Results.Where(predicate).ToList();
        var results = new PagedResult<StorageDrive>(pageOptions, 1, storageDrives);
        return results;
    }

    public async Task<PagedResult<StorageDrive>> GetDrivesAsync(GuidId type, PageOptions pageOptions, IOdinContext odinContext)
    {
        Func<StorageDrive, bool> predicate = drive => drive.TargetDriveInfo.Type == type;

        if (odinContext.Caller.IsAnonymous)
        {
            predicate = drive => drive.TargetDriveInfo.Type == type && drive.AllowAnonymousReads && drive.OwnerOnly == false;
        }

        var page = await GetDrivesInternalAsync(false, pageOptions, odinContext);
        var storageDrives = page.Results.Where(predicate).ToList();
        var results = new PagedResult<StorageDrive>(pageOptions, 1, storageDrives);
        return results;
    }

    public async Task<PagedResult<StorageDrive>> GetAnonymousDrivesAsync(PageOptions pageOptions, IOdinContext odinContext)
    {
        var page = await GetDrivesInternalAsync(false, pageOptions, odinContext);
        var storageDrives = page.Results.Where(drive => drive.AllowAnonymousReads).ToList();
        var results = new PagedResult<StorageDrive>(pageOptions, 1, storageDrives);
        return results;
    }

    private async Task<StorageDriveData?> GetDriveInternal(Guid driveId)
    {
        var record = await _tableDrives.GetAsync(driveId);
        if (record == null)
        {
            return null;
        }

        var drive = ToStorageDriveData(record);
        return drive;
    }

    private static DrivesRecord ToRecord(StorageDriveData storageDrive)
    {
        var details = new StorageDriveDetails
        {
            Metadata = storageDrive.Metadata,
            OwnerOnly = storageDrive.OwnerOnly,
            TargetDriveInfo = storageDrive.TargetDriveInfo,
            IsReadonly = storageDrive.IsReadonly,
            AllowAnonymousReads = storageDrive.AllowAnonymousReads,
            AllowSubscriptions = storageDrive.AllowSubscriptions,
            Attributes = storageDrive.Attributes,
            IsArchived = storageDrive.IsArchived
        };

        var record = new DrivesRecord
        {
            DriveId = storageDrive.Id,
            DriveName = storageDrive.Name,
            DriveType = storageDrive.TargetDriveInfo.Type.Value,
            MasterKeyEncryptedStorageKeyJson = OdinSystemSerializer.Serialize(storageDrive.MasterKeyEncryptedStorageKey),
            EncryptedIdIv64 = storageDrive.EncryptedIdIv.ToBase64(),
            EncryptedIdValue64 = storageDrive.EncryptedIdValue.ToBase64(),
            detailsJson = OdinSystemSerializer.Serialize(details),
            StorageKeyCheckValue = storageDrive.TempOriginalDriveId
        };

        return record;
    }

    private async Task<PagedResult<StorageDrive>> GetDrivesInternalAsync(
        bool enforceSecurity,
        PageOptions pageOptions,
        IOdinContext odinContext)
    {
        var allDrivesData = await _driveCache.GetOrSetAsync(
            CacheKeyAllDrives,
            async _ =>
            {
                var (drives, _, _) = await _tableDrives.GetList(int.MaxValue, null);
                return drives.Select(ToStorageDriveData);
            },
            CacheTtl,
            RootInvalidationTag);

        var allDrives = allDrivesData.Select(ToStorageDrive).ToList();

        // only show archived drives to the owner console
        var shouldFilterArchivedDrive = odinContext?.Caller == null || odinContext.Caller.HasMasterKey == false;
        if (shouldFilterArchivedDrive)
        {
            allDrives = allDrives.Where(d => !d.IsArchived).ToList();
        }


        var caller = odinContext?.Caller;
        if (caller?.IsOwner ?? false)
        {
            return new PagedResult<StorageDrive>(pageOptions, 1, allDrives);
        }


        var level = caller?.SecurityLevel ?? SecurityGroupType.Anonymous;
        if (level == SecurityGroupType.System)
        {
            return new PagedResult<StorageDrive>(pageOptions, 1, allDrives);
        }

        Func<StorageDrive, bool> predicate = drive => drive.OwnerOnly == false;
        if (enforceSecurity)
        {
            if (caller is { IsAnonymous: true }) //default to anonymous
            {
                predicate = drive => drive.AllowAnonymousReads && drive.OwnerOnly == false;
            }
        }

        var result = new PagedResult<StorageDrive>(pageOptions, 1, allDrives.Where(predicate).ToList());
        return result;
    }

    private StorageDriveData ToStorageDriveData(DrivesRecord record)
    {
        var driveDetails = OdinSystemSerializer.Deserialize<StorageDriveDetails>(record.detailsJson);

        var sdd = new StorageDriveData
        {
            Id = record.DriveId,
            TempOriginalDriveId = record.StorageKeyCheckValue,
            Name = record.DriveName,
            TargetDriveInfo = new TargetDrive
            {
                Alias = driveDetails?.TargetDriveInfo.Alias ?? throw new OdinSystemException("driveDetails is null"),
                Type = record.DriveType
            },

            MasterKeyEncryptedStorageKey = OdinSystemSerializer.Deserialize<SymmetricKeyEncryptedAes>(
                record.MasterKeyEncryptedStorageKeyJson),

            EncryptedIdIv = record.EncryptedIdIv64.FromBase64(),
            EncryptedIdValue = record.EncryptedIdValue64.FromBase64(),

            Metadata = driveDetails.Metadata,
            OwnerOnly = driveDetails.OwnerOnly,
            IsReadonly = driveDetails.IsReadonly,
            AllowAnonymousReads = driveDetails.AllowAnonymousReads,
            AllowSubscriptions = driveDetails.AllowSubscriptions,
            Attributes = driveDetails.Attributes,
            IsArchived = driveDetails.IsArchived
        };

        return sdd;
    }

    private StorageDrive ToStorageDrive(StorageDriveData sdd)
    {
        return new StorageDrive(_tenantContext.TenantPathManager, sdd);
    }
}