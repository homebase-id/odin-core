using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Odin.Core.Exceptions;
using Odin.Core.Storage;
using Odin.Core.Storage.Database.Identity;
using Odin.Core.Time;
using Odin.Services.Apps;
using Odin.Services.Authentication.Owner;
using Odin.Services.Authorization.Apps;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Base;
using Odin.Services.Configuration.Eula;
using Odin.Services.Drives;
using Odin.Services.Drives.Management;
using Odin.Services.EncryptionKeyService;
using Odin.Services.Membership.CircleMembership;
using Odin.Services.Membership.Circles;
using Odin.Services.Membership.Connections;
using Odin.Services.Registry;
using Odin.Services.Util;

namespace Odin.Services.Configuration;

/// <summary>
/// Manages initial setup and system configuration for the identity and owner-app
/// </summary>
public class TenantConfigService
{
    private const string ConfigContextKey = "b9e1c2a3-e0e0-480e-a696-ce602b052d07";
    private static readonly SingleKeyValueStorage ConfigStorage = TenantSystemStorage.CreateSingleKeyValueStorage(Guid.Parse(ConfigContextKey));

    private readonly CircleNetworkService _dbs;
    private readonly TenantContext _tenantContext;
    private readonly IIdentityRegistry _registry;
    private readonly DriveManager _driveManager;
    private readonly PublicPrivateKeyService _publicPrivateKeyService;
    private readonly RecoveryService _recoverService;
    private readonly IcrKeyService _icrKeyService;
    private readonly CircleMembershipService _circleMembershipService;
    private readonly IAppRegistrationService _appRegistrationService;
    private readonly IdentityDatabase _identityDatabase;

    public TenantConfigService(
        CircleNetworkService dbs,
        TenantContext tenantContext,
        IIdentityRegistry registry,
        DriveManager driveManager,
        PublicPrivateKeyService publicPrivateKeyService,
        IcrKeyService icrKeyService,
        RecoveryService recoverService,
        CircleMembershipService circleMembershipService,
        IAppRegistrationService appRegistrationService,
        IdentityDatabase identityDatabase)
    {
        _dbs = dbs;
        _tenantContext = tenantContext;
        _registry = registry;
        _driveManager = driveManager;
        _publicPrivateKeyService = publicPrivateKeyService;
        _icrKeyService = icrKeyService;
        _recoverService = recoverService;
        _circleMembershipService = circleMembershipService;
        _appRegistrationService = appRegistrationService;
        _identityDatabase = identityDatabase;
    }

    public async Task InitializeAsync()
    {
        var tenantSettings = await GetTenantSettingsAsync();
        _tenantContext.UpdateSystemConfig(tenantSettings);
    }

    public async Task<TenantVersionInfo> ForceVersionNumberAsync(int version)
    {
        TenantVersionInfo newVersion = new TenantVersionInfo()
        {
            DataVersionNumber = version,
            LastUpgraded = UnixTimeUtc.Now().milliseconds
        };

        await ConfigStorage.UpsertAsync(_identityDatabase.KeyValue, TenantVersionInfo.Key, newVersion);

        return newVersion;
    }

    /// <summary>
    /// Increments the version number and returns the new version
    /// </summary>
    public async Task<TenantVersionInfo> IncrementVersionAsync()
    {


        TenantVersionInfo newVersion = null;
        //TODO CONNECTIONS
        // cn.CreateCommitUnitOfWork(() =>
        {
            var currentVersion = await ConfigStorage.GetAsync<TenantVersionInfo>(_identityDatabase.KeyValue, TenantVersionInfo.Key) ?? new TenantVersionInfo()
            {
                DataVersionNumber = 0,
                LastUpgraded = 0
            };

            newVersion = new TenantVersionInfo()
            {
                DataVersionNumber = ++currentVersion.DataVersionNumber,
                LastUpgraded = UnixTimeUtc.Now().milliseconds
            };

            await ConfigStorage.UpsertAsync(_identityDatabase.KeyValue, TenantVersionInfo.Key, newVersion);
        }
        //);

        return newVersion;
    }

    /// <summary>
    /// Increments the version number and returns the new version
    /// </summary>
    public async Task SetVersionFailureInfoAsync(int dataVersionNumber)
    {
        //TODO CONNECTIONS
        // cn.CreateCommitUnitOfWork(() =>
        {
            var info = new FailedUpgradeVersionInfo
            {
                FailedDataVersionNumber = dataVersionNumber,
                BuildVersion = ReleaseVersionInfo.BuildVersion,
                LastAttempted = UnixTimeUtc.Now().milliseconds
            };

            await ConfigStorage.UpsertAsync(_identityDatabase.KeyValue, FailedUpgradeVersionInfo.Key, info);
        }
        //);
    }

    public async Task<FailedUpgradeVersionInfo> GetVersionFailureInfoAsync()
    {
        return await ConfigStorage.GetAsync<FailedUpgradeVersionInfo>(_identityDatabase.KeyValue, FailedUpgradeVersionInfo.Key);
    }

    public async Task<TenantVersionInfo> GetVersionInfoAsync()
    {

        var info = await ConfigStorage.GetAsync<TenantVersionInfo>(_identityDatabase.KeyValue, TenantVersionInfo.Key);
        return info ?? new TenantVersionInfo
        {
            DataVersionNumber = 0,
            LastUpgraded = 0
        };
    }

    public async Task<bool> IsIdentityServerConfiguredAsync()
    {


        //ok for anonymous to query this as long as we're only returning a bool
        var firstRunInfo = await ConfigStorage.GetAsync<FirstRunInfo>(_identityDatabase.KeyValue, FirstRunInfo.Key);
        return firstRunInfo != null;
    }

    public async Task<bool> IsEulaSignatureRequiredAsync(IOdinContext odinContext)
    {


        odinContext.Caller.AssertHasMasterKey();

        var info = await ConfigStorage.GetAsync<List<EulaSignature>>(_identityDatabase.KeyValue, EulaSystemInfo.StorageKey);
        if (info == null || !info.Any())
        {
            return true;
        }

        var signature = info.SingleOrDefault(signature => signature.Version == EulaSystemInfo.RequiredVersion);
        return signature == null;
    }

    public EulaVersionResponse GetRequiredEulaVersion(IOdinContext odinContext)
    {
        odinContext.Caller.AssertHasMasterKey();
        return new EulaVersionResponse()
        {
            Version = EulaSystemInfo.RequiredVersion
        };
    }

    public async Task<List<EulaSignature>> GetEulaSignatureHistoryAsync(IOdinContext odinContext)
    {


        odinContext.Caller.AssertHasMasterKey();

        var signatures = await ConfigStorage.GetAsync<List<EulaSignature>>(_identityDatabase.KeyValue, EulaSystemInfo.StorageKey) ?? new List<EulaSignature>();

        return signatures;
    }

    public async Task MarkEulaSignedAsync(MarkEulaSignedRequest request, IOdinContext odinContext)
    {


        odinContext.Caller.AssertHasMasterKey();

        OdinValidationUtils.AssertNotNull(request, nameof(request));
        OdinValidationUtils.AssertNotNullOrEmpty(request.Version, nameof(request.Version));

        if (request.Version != EulaSystemInfo.RequiredVersion)
        {
            throw new OdinClientException("Invalid Eula version");
        }

        var signatures = await ConfigStorage.GetAsync<List<EulaSignature>>(_identityDatabase.KeyValue, EulaSystemInfo.StorageKey) ?? new List<EulaSignature>();

        signatures.Add(new EulaSignature()
        {
            SignatureDate = UnixTimeUtc.Now(),
            Version = request.Version,
            SignatureBytes = request.SignatureBytes
        });

        await ConfigStorage.UpsertAsync(_identityDatabase.KeyValue, Eula.EulaSystemInfo.StorageKey, signatures);
    }

    public async Task CreateInitialKeysAsync(IOdinContext odinContext)
    {
        odinContext.Caller.AssertHasMasterKey();
        await _recoverService.CreateInitialKeyAsync(odinContext);
        await _icrKeyService.CreateInitialKeysAsync(odinContext);
        await _publicPrivateKeyService.CreateInitialKeysAsync(odinContext);
    }

    /// <summary>
    /// Configures aspects of the owner's identity that require the master key
    /// </summary>
    public async Task EnsureInitialOwnerSetupAsync(InitialSetupRequest request, IOdinContext odinContext)
    {
        odinContext.Caller.AssertHasMasterKey();

        if (request.FirstRunToken.HasValue)
        {
            await _registry.MarkRegistrationComplete(request.FirstRunToken.GetValueOrDefault());
        }

        //Note: the order here is important.  if the request or system drives include any anonymous
        //drives, they should be added after the system circle exists
        await _circleMembershipService.CreateSystemCirclesAsync(odinContext);

        await EnsureSystemDrivesExist(odinContext);

        foreach (var rd in request.Drives ?? new List<CreateDriveRequest>())
        {
            await CreateDriveIfNotExistsAsync(rd, odinContext);
        }

        //Create additional circles last in case they rely on any of the drives above
        foreach (var rc in request.Circles ?? new List<CreateCircleRequest>())
        {
            await CreateCircleIfNotExistsAsync(rc, odinContext);
        }

        await this.EnsureBuiltInApps(odinContext);

        await using var tx = await _identityDatabase.BeginStackedTransactionAsync();

        var keyValuePairs = new List<(Guid key, object value)>
        {
            (TenantSettings.ConfigKey, TenantSettings.Default),
            (FirstRunInfo.Key, new FirstRunInfo() { FirstRunDate = UnixTimeUtc.Now().milliseconds })
        };

        await ConfigStorage.UpsertManyAsync(_identityDatabase.KeyValue, keyValuePairs);

        tx.Commit();
    }

    public async Task EnsureSystemDrivesExist(IOdinContext odinContext)
    {
        // Note - if the drive attributes was changed, they will be applied by this
        await CreateDriveIfNotExistsAsync(SystemDriveConstants.CreateChatDriveRequest, odinContext);
        await CreateDriveIfNotExistsAsync(SystemDriveConstants.CreateMailDriveRequest, odinContext);
        await CreateDriveIfNotExistsAsync(SystemDriveConstants.CreateFeedDriveRequest, odinContext);
        await CreateDriveIfNotExistsAsync(SystemDriveConstants.CreateHomePageConfigDriveRequest, odinContext);
        await CreateDriveIfNotExistsAsync(SystemDriveConstants.CreatePublicPostsChannelDriveRequest, odinContext);

        await CreateDriveIfNotExistsAsync(SystemDriveConstants.CreateContactDriveRequest, odinContext);
        await CreateDriveIfNotExistsAsync(SystemDriveConstants.CreateProfileDriveRequest, odinContext);
        await CreateDriveIfNotExistsAsync(SystemDriveConstants.CreateWalletDriveRequest, odinContext);
        await CreateDriveIfNotExistsAsync(SystemDriveConstants.CreateTransientTempDriveRequest, odinContext);
    }

    public async Task UpdateSystemFlagAsync(UpdateFlagRequest request, IOdinContext odinContext)
    {


        odinContext.Caller.AssertHasMasterKey();

        if (!Enum.TryParse(typeof(TenantConfigFlagNames), request.FlagName, true, out var flag))
        {
            throw new OdinClientException("Invalid flag name", OdinClientErrorCode.InvalidFlagName);
        }

        var cfg = await ConfigStorage.GetAsync<TenantSettings>(_identityDatabase.KeyValue, TenantSettings.ConfigKey) ?? new TenantSettings();

        switch (flag)
        {
            case TenantConfigFlagNames.AnonymousVisitorsCanViewWhoIFollow:
                cfg.AnonymousVisitorsCanViewWhoIFollow = bool.Parse(request.Value);
                break;

            case TenantConfigFlagNames.AuthenticatedIdentitiesCanViewWhoIFollow:
                cfg.AuthenticatedIdentitiesCanViewWhoIFollow = bool.Parse(request.Value);
                break;

            case TenantConfigFlagNames.ConnectedIdentitiesCanViewWhoIFollow:
                cfg.AllConnectedIdentitiesCanViewWhoIFollow = bool.Parse(request.Value);
                await UpdateSystemCirclePermissionAsync(PermissionKeys.ReadWhoIFollow, cfg.AllConnectedIdentitiesCanViewWhoIFollow,
                    odinContext);
                break;

            case TenantConfigFlagNames.AnonymousVisitorsCanViewConnections:
                cfg.AnonymousVisitorsCanViewConnections = bool.Parse(request.Value);
                break;

            case TenantConfigFlagNames.AuthenticatedIdentitiesCanViewConnections:
                cfg.AuthenticatedIdentitiesCanViewConnections = bool.Parse(request.Value);
                break;

            case TenantConfigFlagNames.ConnectedIdentitiesCanViewConnections:
                cfg.AllConnectedIdentitiesCanViewConnections = bool.Parse(request.Value);
                await UpdateSystemCirclePermissionAsync(PermissionKeys.ReadConnections, cfg.AllConnectedIdentitiesCanViewConnections,
                    odinContext);
                break;

            case TenantConfigFlagNames.AuthenticatedIdentitiesCanReactOnAnonymousDrives:
                cfg.AuthenticatedIdentitiesCanReactOnAnonymousDrives = bool.Parse(request.Value);
                break;

            case TenantConfigFlagNames.AuthenticatedIdentitiesCanCommentOnAnonymousDrives:
                cfg.AuthenticatedIdentitiesCanCommentOnAnonymousDrives = bool.Parse(request.Value);
                break;

            case TenantConfigFlagNames.ConnectedIdentitiesCanReactOnAnonymousDrives:
                cfg.ConnectedIdentitiesCanReactOnAnonymousDrives = bool.Parse(request.Value);
                break;

            case TenantConfigFlagNames.ConnectedIdentitiesCanCommentOnAnonymousDrives:
                cfg.ConnectedIdentitiesCanCommentOnAnonymousDrives = bool.Parse(request.Value);
                break;

            case TenantConfigFlagNames.DisableAutoAcceptIntroductions:
                cfg.DisableAutoAcceptIntroductions = bool.Parse(request.Value);
                break;

            default:
                throw new OdinClientException("Flag name is valid but not handled",
                    OdinClientErrorCode.UnknownFlagName);
        }

        await ConfigStorage.UpsertAsync(_identityDatabase.KeyValue, TenantSettings.ConfigKey, cfg);

        //TODO: eww, use mediator instead
        _tenantContext.UpdateSystemConfig(cfg);
    }


    public async Task<TenantSettings> GetTenantSettingsAsync()
    {

        return await ConfigStorage.GetAsync<TenantSettings>(_identityDatabase.KeyValue, TenantSettings.ConfigKey) ?? TenantSettings.Default;
    }

    public async Task<OwnerAppSettings> GetOwnerAppSettingsAsync(IOdinContext odinContext)
    {


        odinContext.Caller.AssertHasMasterKey();
        return await ConfigStorage.GetAsync<OwnerAppSettings>(_identityDatabase.KeyValue, OwnerAppSettings.ConfigKey) ?? OwnerAppSettings.Default;
    }

    public async Task UpdateOwnerAppSettingsAsync(OwnerAppSettings newSettings, IOdinContext odinContext)
    {

        odinContext.Caller.AssertHasMasterKey();
        await ConfigStorage.UpsertAsync(_identityDatabase.KeyValue, OwnerAppSettings.ConfigKey, newSettings);
    }

    //

    public async Task EnsureBuiltInApps(IOdinContext odinContext)
    {
        await RegisterChatAppAsync(odinContext);
        await RegisterMailAppAsync(odinContext);
        await RegisterFeedApp(odinContext);
        // await RegisterPhotosApp();
    }

    private async Task RegisterFeedApp(IOdinContext odinContext)
    {
        var request = new AppRegistrationRequest()
        {
            AppId = SystemAppConstants.FeedAppId,
            Name = "Homebase - Feed",
            AuthorizedCircles = new List<Guid>(),
            CircleMemberPermissionGrant = null,
            Drives =
            [
                new()
                {
                    PermissionedDrive = new PermissionedDrive()
                    {
                        Drive = SystemDriveConstants.FeedDrive,
                        Permission = DrivePermission.ReadWrite
                    }
                },
                new()
                {
                    PermissionedDrive = new PermissionedDrive()
                    {
                        Drive = SystemDriveConstants.ContactDrive,
                        Permission = DrivePermission.ReadWrite
                    }
                },
                new()
                {
                    PermissionedDrive = new PermissionedDrive()
                    {
                        Drive = SystemDriveConstants.ProfileDrive,
                        Permission = DrivePermission.Read
                    }
                },
                new()
                {
                    PermissionedDrive = new PermissionedDrive()
                    {
                        Drive = SystemDriveConstants.HomePageConfigDrive,
                        Permission = DrivePermission.Read
                    }
                },
                new()
                {
                    PermissionedDrive = new PermissionedDrive()
                    {
                        Drive = SystemDriveConstants.PublicPostsChannelDrive,
                        Permission = DrivePermission.All
                    }
                }
            ],
            PermissionSet = new PermissionSet(
                PermissionKeys.ReadConnections,
                PermissionKeys.ReadCircleMembership,
                PermissionKeys.SendPushNotifications,
                PermissionKeys.ReadWhoIFollow,
                PermissionKeys.ReadMyFollowers,
                PermissionKeys.ManageFeed,
                PermissionKeys.ReadConnectionRequests,
                PermissionKeys.UseTransitRead,
                PermissionKeys.PublishStaticContent,
                PermissionKeys.UseTransitWrite)
        };

        var existingApp = await _appRegistrationService.GetAppRegistration(request.AppId, odinContext);
        if (existingApp == null)
        {
            await _appRegistrationService.RegisterAppAsync(request, odinContext);
        }
    }

    private async Task RegisterChatAppAsync(IOdinContext odinContext)
    {
        var existingApp =
            await _appRegistrationService.GetAppRegistration(SystemAppConstants.ChatAppRegistrationRequest.AppId, odinContext);
        if (null == existingApp)
        {
            await _appRegistrationService.RegisterAppAsync(SystemAppConstants.ChatAppRegistrationRequest, odinContext);
        }
    }

    private async Task RegisterMailAppAsync(IOdinContext odinContext)
    {
        var existingApp =
            await _appRegistrationService.GetAppRegistration(SystemAppConstants.MailAppRegistrationRequest.AppId, odinContext);
        if (null == existingApp)
        {
            await _appRegistrationService.RegisterAppAsync(SystemAppConstants.MailAppRegistrationRequest, odinContext);
        }
    }

    private async Task<bool> CreateCircleIfNotExistsAsync(CreateCircleRequest request, IOdinContext odinContext)
    {
        var existingCircleDef = await _circleMembershipService.GetCircleAsync(request.Id, odinContext);
        if (null == existingCircleDef)
        {
            await _circleMembershipService.CreateCircleDefinitionAsync(request, odinContext);
            return true;
        }

        return false;
    }

    private async Task<bool> CreateDriveIfNotExistsAsync(CreateDriveRequest request, IOdinContext odinContext)
    {


        var drive = await _driveManager.GetDriveIdByAliasAsync(request.TargetDrive, false);

        if (null == drive)
        {
            await _driveManager.CreateDriveAsync(request, odinContext);
            return true;
        }

        return false;
    }

    private async Task UpdateSystemCirclePermissionAsync(int key, bool shouldGrantKey, IOdinContext odinContext)
    {
        var systemCircle = await _circleMembershipService.GetCircleAsync(SystemCircleConstants.ConfirmedConnectionsCircleId, odinContext);

        if (shouldGrantKey)
        {
            if (!systemCircle.Permissions.Keys.Contains(key))
            {
                systemCircle.Permissions.Keys.Add(key);
            }
        }
        else
        {
            if (systemCircle.Permissions.Keys.Contains(key))
            {
                systemCircle.Permissions.Keys.Remove(key);
            }
        }

        await _dbs.UpdateCircleDefinitionAsync(systemCircle, odinContext);
    }
}
