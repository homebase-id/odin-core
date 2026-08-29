using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.Extensions.Logging;
using Odin.Core;
using Odin.Core.Cryptography.Crypto;
using Odin.Core.Cryptography.Data;
using Odin.Core.Exceptions;
using Odin.Core.Serialization;
using Odin.Core.Storage.Cache;
using Odin.Core.Storage.Database.Identity.Connection;
using Odin.Core.Storage.Database.Identity.Table;
using Odin.Services.Apps.Builtin;
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
    private readonly ScopedIdentityConnectionFactory _scopedConnectionFactory;

    /// <summary>
    /// Manages drive creation, metadata updates, and their overall definitions
    /// </summary>
    public DriveManager(
        ILogger<DriveManager> logger,
        ITenantLevel2Cache driveCache,
        IMediator mediator,
        TenantContext tenantContext,
        TableDrivesCached tableDrives,
        ScopedIdentityConnectionFactory scopedConnectionFactory)
    {
        _logger = logger;
        _driveCache = driveCache;
        _mediator = mediator;
        _tenantContext = tenantContext;
        _tableDrives = tableDrives;
        _scopedConnectionFactory = scopedConnectionFactory;
    }

    // The mediator handlers for DriveDefinitionAddedNotification mutate caches (e.g.
    // OdinContextCache) that other connections will populate from the database. If we
    // publish mid-transaction, those caches can be rebuilt against an uncommitted
    // snapshot that doesn't yet contain the new drive, then stay stale until TTL.
    // Defer until commit so observers only see committed state.
    private Task PublishDriveDefinitionAddedAsync(DriveDefinitionAddedNotification notification)
    {
        if (_scopedConnectionFactory.HasTransaction)
        {
            _scopedConnectionFactory.AddPostCommitAction(() => _mediator.Publish(notification));
            return Task.CompletedTask;
        }

        return _mediator.Publish(notification);
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

        // A whitespace-only value means "not set", the same as null or empty. Clients serialize an
        // unset field as "" or " " routinely, and without this the three spellings diverge: null and
        // "" derive a slug while "   " fails validation and throws. Note this is not coercion of a
        // real slug -- there is no address inside "   " to preserve. Anything with actual content is
        // still validated and rejected on failure, so " chat " is an error, never trimmed to "chat".
        var requestedSlug = string.IsNullOrWhiteSpace(request.DriveSlug) ? null : request.DriveSlug;
        var requestedTypeSlug = string.IsNullOrWhiteSpace(request.DriveTypeSlug) ? null : request.DriveTypeSlug;

        // Format only, and only when supplied. A slug is a URL segment and a wire address other
        // identities resolve against, so a malformed one is rejected rather than coerced -- silently
        // lowercasing or stripping produces an address the caller did not ask for.
        OdinSlug.AssertValidOrNull(requestedSlug, nameof(request.DriveSlug));
        OdinSlug.AssertValidOrNull(requestedTypeSlug, nameof(request.DriveTypeSlug));

        // AppId is taken on trust: it is never resolved, and the owning app is NOT required to exist.
        // Provisioning creates drives before it registers apps (BuiltinProvisioner.EnsureAllAsync),
        // because a registration is granted drives and a grant cannot be issued for a drive that is not
        // there. Validating the app here would invert that and break identity setup. The reverse
        // dependency is the real one; this direction must stay unchecked.
        //
        // The caller's values win; only a missing one is derived. A supplied slug is never replaced --
        // it is an address, so handing back a different one would be worse than refusing.
        //
        // Nothing is derived for a drive with no owning app. The invariant is that AppId and DriveSlug
        // are set together or both NULL (docs/drive-addressing.md, Schema): NULLs are distinct in a
        // unique index in both dialects, so a slug on an AppId-less row is unconstrained and two drives
        // could claim the same one. Every drive is expected to carry an AppId -- system drives included,
        // under the system app -- so in practice this guard does not fire; it is what keeps the
        // invariant true for anything that slips through without one.
        var driveSlug = requestedSlug;
        var driveTypeSlug = requestedTypeSlug;

        if (request.AppId != null)
        {
            if (driveSlug == null)
            {
                // Only read the table when there is actually something to derive, and scope the taken
                // set to this app: the constraint is per app, so feed/news and chat/news may coexist.
                // Deduping across the whole identity would hand the second one "news-2" -- a permanent
                // address nobody asked for, for a collision the schema permits.
                var (existingDrives, _, _) = await _tableDrives.GetList(int.MaxValue, null);
                var taken = new HashSet<string>(
                    existingDrives
                        .Where(d => d.AppId == request.AppId && !string.IsNullOrWhiteSpace(d.DriveSlug))
                        .Select(d => d.DriveSlug),
                    StringComparer.Ordinal);

                driveSlug = DriveSlugGenerator.Generate(request.TargetDrive.Alias.Value, request.Name, taken);
            }

            driveTypeSlug ??= DriveSlugGenerator.TypeSlugFor(request.TargetDrive.Alias.Value, request.TargetDrive.Type.Value);
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
            AllowCdn = request.AllowCdn,
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
            StorageKeyCheckValue = id,

            // Columns, not details -- see ToRecord. The slugs are the caller's or derived above; AppId is
            // still the caller's alone, since nothing decides drive ownership yet.
            AppId = request.AppId,
            DriveSlug = driveSlug,
            DriveTypeSlug = driveTypeSlug
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

        await PublishDriveDefinitionAddedAsync(new DriveDefinitionAddedNotification
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

        if (BuiltinDrives.Protected.Any(d => d == storageDrive.TargetDriveInfo))
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

            await PublishDriveDefinitionAddedAsync(new DriveDefinitionAddedNotification
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

        if (BuiltinDrives.Protected.Any(d => d == storageDrive.TargetDriveInfo))
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

            await PublishDriveDefinitionAddedAsync(new DriveDefinitionAddedNotification
            {
                IsNewDrive = false,
                Drive = storageDrive,
                OdinContext = odinContext
            });
        }
    }

    public async Task SetDriveAllowCdnAsync(Guid driveId, bool allowCdn, IOdinContext odinContext)
    {
        odinContext.Caller.AssertHasMasterKey();

        var storageDrive = await GetDriveAsync(driveId);
        if (storageDrive == null)
        {
            throw new OdinClientException($"Invalid drive id {driveId}", OdinClientErrorCode.InvalidDrive);
        }

        // Deliberately no system-drive or owner-only guard here, unlike the sibling setters. The
        // rule this flag replaced put every drive - system drives and owner-only drives included -
        // in the CDN list, so refusing either here would take away something that already works.
        // Tightening that is a separate decision, not a side effect of introducing the flag.

        //only change if needed
        if (storageDrive.IsCdnEnabled() != allowCdn)
        {
            storageDrive.SetCdnEnabled(allowCdn);

            await _tableDrives.UpsertAsync(ToRecord(storageDrive.Data));

            await PublishDriveDefinitionAddedAsync(new DriveDefinitionAddedNotification
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

        if (BuiltinDrives.Protected.Any(d => d == storageDrive.TargetDriveInfo))
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

            var freshAndNewRecord = await _tableDrives.GetDriveDirectAsync(driveId);
            var cachedRecord = await _tableDrives.GetAsync(driveId);

            _logger.LogDebug("freshAndCleanClean: [{fresh}]", freshAndNewRecord?.detailsJson ?? "empty");
            _logger.LogDebug("cachedVersion: [{cache}]", cachedRecord?.detailsJson ?? "empty");

            if (affected != 1)
            {
                throw new OdinSystemException($"Archive drive should have updated 1 and only 1 row.  Number updated: {affected}");
            }

            await PublishDriveDefinitionAddedAsync(new DriveDefinitionAddedNotification
            {
                IsNewDrive = false,
                Drive = storageDrive,
                OdinContext = odinContext,
            });
        }
    }

    /// <summary>
    /// Makes a drive match what the app tree says its address should be.  Migration only.
    /// </summary>
    /// <remarks>
    /// There is no other way in: <see cref="CreateDriveAsync"/> sets these once and nothing updates them,
    /// because a slug is a wire address other identities resolve against.  This exists because the tree
    /// is the source of truth for the drives it declares, and it corrects rather than fills -- a drive
    /// carrying a value an earlier build wrote is moved to what the tree now says.
    /// <para>
    /// Note it does <b>not</b> refuse protected drives, unlike every other setter here.  All fourteen are
    /// protected, so guarding on that would make it useless for the one job it has.
    /// </para>
    /// </remarks>
    internal async Task<bool> ApplyTreeAddressAsync(Guid driveId, Guid appId, string driveSlug,
        string driveTypeSlug, IOdinContext odinContext)
    {
        odinContext.Caller.AssertHasMasterKey();

        OdinSlug.AssertValidOrNull(driveSlug, nameof(driveSlug));
        OdinSlug.AssertValidOrNull(driveTypeSlug, nameof(driveTypeSlug));

        var storageDrive = await GetDriveInternal(driveId);
        if (storageDrive == null)
        {
            throw new OdinClientException($"Invalid drive id {driveId}", OdinClientErrorCode.InvalidDrive);
        }

        if (storageDrive.AppId == appId && storageDrive.DriveSlug == driveSlug &&
            storageDrive.DriveTypeSlug == driveTypeSlug)
        {
            return false;
        }

        storageDrive.AppId = appId;
        storageDrive.DriveSlug = driveSlug;
        storageDrive.DriveTypeSlug = driveTypeSlug;

        await _tableDrives.UpsertAsync(ToRecord(storageDrive));
        return true;
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
        var driveData = _tableDrives.InDatabaseTransaction
            ? await GetDriveInternal(driveId)
            : await _driveCache.GetOrSetAsync(
            CacheKeyDrive + driveId,
            _ => GetDriveInternal(driveId),
            CacheTtl,
            EntrySize.Medium,
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
    
    public async Task<PagedResult<StorageDrive>> GetCdnEnabledDrivesAsync(PageOptions pageOptions, IOdinContext odinContext)
    {
        var page = await GetDrivesInternalAsync(false, pageOptions, odinContext);
        var storageDrives = page.Results.Where(drive => drive.IsCdnEnabled()).ToList();
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
            AllowCdn = storageDrive.AllowCdn,
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
            StorageKeyCheckValue = storageDrive.TempOriginalDriveId,

            // Columns, not details: UNIQUE(identityId, AppId, DriveSlug) constrains these, and a copy
            // inside detailsJson could drift from what the constraint is enforcing.
            AppId = storageDrive.AppId,
            DriveSlug = storageDrive.DriveSlug,
            DriveTypeSlug = storageDrive.DriveTypeSlug
        };

        return record;
    }

    private async Task<PagedResult<StorageDrive>> GetDrivesInternalAsync(
        bool enforceSecurity,
        PageOptions pageOptions,
        IOdinContext odinContext)
    {
        async Task<IEnumerable<StorageDriveData>> AllDrivesDataReader(CancellationToken _)
        {
            var (drives, _, _) = await _tableDrives.GetList(int.MaxValue, null);
            // Materialize: this result is handed to the cache, and a deferred Select would re-run the
            // (JSON-deserializing) projection on every enumeration, i.e. on every cache hit.
            return drives.Select(ToStorageDriveData).ToList();
        }

        var allDrivesData = _tableDrives.InDatabaseTransaction
            ? await AllDrivesDataReader(CancellationToken.None)
            : await _driveCache.GetOrSetAsync(
            CacheKeyAllDrives,
            AllDrivesDataReader,
            CacheTtl,
            EntrySize.Medium,
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
            AllowCdn = driveDetails.AllowCdn,
            Attributes = driveDetails.Attributes,
            IsArchived = driveDetails.IsArchived,

            AppId = record.AppId,
            DriveSlug = record.DriveSlug,
            DriveTypeSlug = record.DriveTypeSlug
        };

        return sdd;
    }

    private StorageDrive ToStorageDrive(StorageDriveData sdd)
    {
        return new StorageDrive(_tenantContext.TenantPathManager, sdd);
    }

}