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
using Odin.Core.Storage;
using Odin.Core.Storage.Database.Identity.Table;
using Odin.Core.Util;
using Odin.Services.Authorization.Acl;
using Odin.Services.Base;
using Odin.Services.Mediator;
using Odin.Services.Util;

[assembly: InternalsVisibleTo("Odin.Hosting")]

namespace Odin.Services.Drives.Management;

/// <summary>
/// Manages drive creation, metadata updates, and their overall definitions
/// </summary>
public class DriveManager : IDriveManager
{
    private readonly ILogger<DriveManager> _logger;
    private readonly SharedConcurrentDictionary<DriveManager, Guid, StorageDrive> _driveCache;

    private readonly IMediator _mediator;
    private readonly TenantContext _tenantContext;
    private readonly TableDrivesCached _tableDrives;

    /// <summary>
    /// Manages drive creation, metadata updates, and their overall definitions
    /// </summary>
    public DriveManager(ILogger<DriveManager> logger,
        SharedConcurrentDictionary<DriveManager, Guid, StorageDrive> driveCache,
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
        if (string.IsNullOrEmpty(request?.Name))
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
            DriveAlias = request.TargetDrive.Alias.Value,
            MasterKeyEncryptedStorageKeyJson = OdinSystemSerializer.Serialize(driveKey),
            EncryptedIdIv64 = encryptedIdIv.ToBase64(),
            EncryptedIdValue64 = encryptedIdValue.ToBase64(),
            detailsJson = OdinSystemSerializer.Serialize(driveData),
            TempOriginalDriveId = id
        };

        try
        {
            await _tableDrives.InsertAsync(record);
        }
        catch (OdinDatabaseException e)
        {
            if (e.IsUniqueConstraintViolation)
            {
                throw new OdinClientException("Existing drive", OdinClientErrorCode.InvalidDrive);
            }
        }
        finally
        {
            storageKey.Wipe();
        }

        var storageDrive = ToStorageDrive(record);
        storageDrive.CreateDirectories();

        _logger.LogDebug($"Created a new Drive - {storageDrive.TargetDriveInfo}");
        CacheDrive(storageDrive);
        _logger.LogDebug($"End - Created a new Drive - {storageDrive.TargetDriveInfo}");

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
        StorageDrive storageDrive = await GetDriveAsync(driveId);

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

            await _tableDrives.UpsertAsync(ToRecord(storageDrive));

            CacheDrive(storageDrive);

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
        StorageDrive storageDrive = await GetDriveInternal(driveId);

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

            await _tableDrives.UpsertAsync(ToRecord(storageDrive));

            CacheDrive(storageDrive);

            await _mediator.Publish(new DriveDefinitionAddedNotification
            {
                IsNewDrive = false,
                Drive = storageDrive,
                OdinContext = odinContext
            });
        }
    }

    public async Task UpdateMetadataAsync(Guid driveId, string metadata, IOdinContext odinContext)
    {
        odinContext.Caller.AssertHasMasterKey();

        StorageDrive storageDrive = await GetDriveInternal(driveId);
        if (null == storageDrive)
        {
            throw new OdinClientException($"Invalid drive id {driveId}", OdinClientErrorCode.InvalidDrive);
        }
        storageDrive.Metadata = metadata;
        await _tableDrives.UpsertAsync(ToRecord(storageDrive));

        CacheDrive(storageDrive);
    }

    public async Task UpdateAttributesAsync(Guid driveId, Dictionary<string, string> attributes, IOdinContext odinContext)
    {
        odinContext.Caller.AssertHasMasterKey();
        StorageDrive storageDrive = await GetDriveInternal(driveId);
        
        if (null == storageDrive)
        {
            throw new OdinClientException($"Invalid drive id {driveId}", OdinClientErrorCode.InvalidDrive);
        }
        
        storageDrive.Attributes = attributes;

        await _tableDrives.UpsertAsync(ToRecord(storageDrive));

        CacheDrive(storageDrive);
    }

    public async Task<StorageDrive> GetDriveAsync(Guid driveId, bool failIfInvalid = false)
    {
        if (_driveCache.TryGetValue(driveId, out var cachedDrive))
        {
            return cachedDrive;
        }

        var drive = await GetDriveInternal(driveId);
        if (drive == null)
        {
            if (failIfInvalid)
            {
                throw new OdinClientException($"Invalid drive id {driveId}", OdinClientErrorCode.InvalidDrive);
            }

            return null;
        }

        CacheDrive(drive);
        return drive;
    }

    public async Task<PagedResult<StorageDrive>> GetDrivesAsync(PageOptions pageOptions, IOdinContext odinContext)
    {
        Func<StorageDrive, bool> predicate = _ => true;
        if (odinContext.Caller.IsAnonymous)
        {
            predicate = drive => drive.AllowAnonymousReads && drive.OwnerOnly == false;
        }

        var page = await this.GetDrivesInternalAsync(false, pageOptions, odinContext);
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

        var page = await this.GetDrivesInternalAsync(false, pageOptions, odinContext);
        var storageDrives = page.Results.Where(predicate).ToList();
        var results = new PagedResult<StorageDrive>(pageOptions, 1, storageDrives);
        return results;
    }

    public async Task<PagedResult<StorageDrive>> GetAnonymousDrivesAsync(PageOptions pageOptions, IOdinContext odinContext)
    {
        var page = await this.GetDrivesInternalAsync(false, pageOptions, odinContext);
        var storageDrives = page.Results.Where(drive => drive.AllowAnonymousReads).ToList();
        var results = new PagedResult<StorageDrive>(pageOptions, 1, storageDrives);
        return results;
    }

    private async Task<StorageDrive> GetDriveInternal(Guid driveId)
    {
        var record = await _tableDrives.GetAsync(driveId);
        if (null == record)
        {
            return null;
        }

        var drive = ToStorageDrive(record);
        return drive;
    }

    private DrivesRecord ToRecord(StorageDrive storageDrive)
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
        };

        var record = new DrivesRecord
        {
            DriveId = storageDrive.Id,
            DriveName = storageDrive.Name,
            DriveType = storageDrive.TargetDriveInfo.Type.Value,
            DriveAlias = storageDrive.TargetDriveInfo.Alias.Value,
            MasterKeyEncryptedStorageKeyJson = OdinSystemSerializer.Serialize(storageDrive.MasterKeyEncryptedStorageKey),
            EncryptedIdIv64 = storageDrive.EncryptedIdIv.ToBase64(),
            EncryptedIdValue64 = storageDrive.EncryptedIdValue.ToBase64(),
            detailsJson = OdinSystemSerializer.Serialize(details),
            TempOriginalDriveId = storageDrive.TempOriginalDriveId
        };

        return record;
    }

    private async Task<PagedResult<StorageDrive>> GetDrivesInternalAsync(bool enforceSecurity, PageOptions pageOptions,
        IOdinContext odinContext)
    {
        List<StorageDrive> allDrives;

        if (_driveCache.Any())
        {
            allDrives = _driveCache.Values.ToList();
            _logger.LogTrace($"GetDrivesInternal - cache read:  Count: {allDrives.Count}");
        }
        else
        {
            var (storageDrives, _, _) = await _tableDrives.GetList(Int32.MaxValue, null);
            allDrives = storageDrives.Select(ToStorageDrive).ToList();
            _logger.LogTrace($"GetDrivesInternal - disk read:  Count: {allDrives.Count}");
        }

        var caller = odinContext.Caller;
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
            if (caller.IsAnonymous) //default to anonymous 
            {
                predicate = drive => drive.AllowAnonymousReads && drive.OwnerOnly == false;
            }
        }

        var result = new PagedResult<StorageDrive>(pageOptions, 1, allDrives.Where(predicate).ToList());
        return result;
    }

    private StorageDrive ToStorageDrive(DrivesRecord record)
    {
        var driveDetails = OdinSystemSerializer.Deserialize<StorageDriveDetails>(record.detailsJson);

        var sdd = new StorageDriveData()
        {
            Id = record.DriveId,
            TempOriginalDriveId = record.TempOriginalDriveId,
            Name = record.DriveName,
            TargetDriveInfo = new TargetDrive()
            {
                Alias = driveDetails.TargetDriveInfo.Alias,
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
            Attributes = driveDetails.Attributes
        };

        return new StorageDrive(_tenantContext.TenantPathManager, sdd);
    }

    private void CacheDrive(StorageDrive drive)
    {
        _logger.LogTrace("Cached Drive {drive}", drive.TargetDriveInfo);
        // _driveCacheNope.Set(GetCacheKey(drive.Id), drive, TimeSpan.FromMinutes(30));
        _driveCache[drive.Id] = drive;
    }

    public async Task LoadCacheAsync()
    {
        var (storageDrives, _, _) = await _tableDrives.GetList(Int32.MaxValue, null);
        foreach (var drive in storageDrives.Select(ToStorageDrive).ToList())
        {
            CacheDrive(drive);
        }
    }
}