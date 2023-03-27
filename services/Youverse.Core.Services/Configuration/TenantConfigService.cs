using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Dawn;
using Youverse.Core.Exceptions;
using Youverse.Core.Services.Authorization.Apps;
using Youverse.Core.Services.Authorization.ExchangeGrants;
using Youverse.Core.Services.Authorization.Permissions;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Contacts.Circle;
using Youverse.Core.Services.Contacts.Circle.Membership;
using Youverse.Core.Services.Contacts.Circle.Membership.Definition;
using Youverse.Core.Services.Drives;
using Youverse.Core.Services.Drives.Management;
using Youverse.Core.Services.Registry;
using Youverse.Core.Storage;
using TenantStorageConfig = Youverse.Core.Services.Base.TenantStorageConfig;

namespace Youverse.Core.Services.Configuration;

/// <summary>
/// Manages initial setup and system configuration for the identity and owner-app
/// </summary>
public class TenantConfigService
{
    private readonly ICircleNetworkService _cns;
    private readonly DotYouContextAccessor _contextAccessor;
    private readonly TenantContext _tenantContext;
    private readonly SingleKeyValueStorage _configStorage;
    private readonly IIdentityRegistry _registry;
    private readonly IAppRegistrationService _appRegistrationService;
    private readonly DriveManager _driveManager;
    private readonly ITenantSystemStorage _tenantSystemStorage;

    public TenantConfigService(ICircleNetworkService cns, DotYouContextAccessor contextAccessor,
        ITenantSystemStorage storage, TenantContext tenantContext,
        IIdentityRegistry registry, IAppRegistrationService appRegistrationService, DriveManager driveManager, ITenantSystemStorage tenantSystemStorage)
    {
        _cns = cns;
        _contextAccessor = contextAccessor;
        _tenantContext = tenantContext;
        _registry = registry;
        _appRegistrationService = appRegistrationService;
        _driveManager = driveManager;
        _tenantSystemStorage = tenantSystemStorage;
        _configStorage = storage.SingleKeyValueStorage;
        _tenantContext.UpdateSystemConfig(this.GetTenantSettings());
    }

    public bool IsIdentityServerConfigured()
    {
        //ok for anonymous to query this as long as we're only returning a bool
        var firstRunInfo = _configStorage.Get<FirstRunInfo>(FirstRunInfo.Key);
        return firstRunInfo != null;
    }

    /// <summary>
    /// Configures aspects of the owner's identity that require the master key
    /// </summary>
    public async Task EnsureInitialOwnerSetup(InitialSetupRequest request)
    {
        _contextAccessor.GetCurrent().Caller.AssertHasMasterKey();

        if (request.FirstRunToken.HasValue)
        {
            await _registry.MarkRegistrationComplete(request.FirstRunToken.GetValueOrDefault());
        }

        //Note: the order here is important.  if the request or system drives include any anonymous
        //drives, they should be added after the system circle exists
        await _cns.CreateSystemCircle();

        await CreateDriveIfNotExists(SystemDriveConstants.CreateChatDriveRequest);
        await CreateDriveIfNotExists(SystemDriveConstants.CreateContactDriveRequest);
        await CreateDriveIfNotExists(SystemDriveConstants.CreateProfileDriveRequest);
        await CreateDriveIfNotExists(SystemDriveConstants.CreateWalletDriveRequest);
        await CreateDriveIfNotExists(SystemDriveConstants.CreateFeedDriveRequest);

        await CreateDriveIfNotExists(SystemDriveConstants.CreateTransientTempDriveRequest);

        foreach (var rd in request.Drives ?? new List<CreateDriveRequest>())
        {
            await CreateDriveIfNotExists(rd);
        }

        //Create additional circles last in case they rely on any of the drives above
        foreach (var rc in request.Circles ?? new List<CreateCircleRequest>())
        {
            await CreateCircleIfNotExists(rc);
        }

        await this.CreateSystemApps();

        _configStorage.Upsert(TenantSettings.ConfigKey, TenantSettings.Default);

        _configStorage.Upsert(FirstRunInfo.Key, new FirstRunInfo()
        {
            FirstRunDate = UnixTimeUtc.Now().milliseconds
        });
    }

    public void UpdateSystemFlag(UpdateFlagRequest request)
    {
        _contextAccessor.GetCurrent().Caller.AssertHasMasterKey();

        if (!Enum.TryParse(typeof(TenantConfigFlagNames), request.FlagName, true, out var flag))
        {
            throw new YouverseClientException("Invalid flag name", YouverseClientErrorCode.InvalidFlagName);
        }

        var cfg = _configStorage.Get<TenantSettings>(TenantSettings.ConfigKey) ?? new TenantSettings();

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
                this.UpdateSystemCirclePermission(PermissionKeys.ReadWhoIFollow, cfg.AllConnectedIdentitiesCanViewWhoIFollow);
                break;

            case TenantConfigFlagNames.AnonymousVisitorsCanViewConnections:
                cfg.AnonymousVisitorsCanViewConnections = bool.Parse(request.Value);
                break;

            case TenantConfigFlagNames.AuthenticatedIdentitiesCanViewConnections:
                cfg.AuthenticatedIdentitiesCanViewConnections = bool.Parse(request.Value);
                break;

            case TenantConfigFlagNames.ConnectedIdentitiesCanViewConnections:
                cfg.AllConnectedIdentitiesCanViewConnections = bool.Parse(request.Value);
                this.UpdateSystemCirclePermission(PermissionKeys.ReadConnections, cfg.AllConnectedIdentitiesCanViewConnections);
                break;

            default:
                throw new YouverseClientException("Flag name is valid but not handled",
                    YouverseClientErrorCode.UnknownFlagName);
        }

        _configStorage.Upsert(TenantSettings.ConfigKey, cfg);

        //TODO: eww, use mediator instead
        _tenantContext.UpdateSystemConfig(cfg);
    }

    private void UpdateSystemCirclePermission(int key, bool shouldGrantKey)
    {
        var systemCircle = _cns.GetCircleDefinition(CircleConstants.SystemCircleId);


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

        _cns.UpdateCircleDefinition(systemCircle);
    }

    public TenantSettings GetTenantSettings()
    {
        return _configStorage.Get<TenantSettings>(TenantSettings.ConfigKey) ?? TenantSettings.Default;
    }

    public OwnerAppSettings GetOwnerAppSettings()
    {
        _contextAccessor.GetCurrent().Caller.AssertHasMasterKey();
        return _configStorage.Get<OwnerAppSettings>(OwnerAppSettings.ConfigKey) ?? OwnerAppSettings.Default;
    }

    public void UpdateOwnerAppSettings(OwnerAppSettings newSettings)
    {
        _contextAccessor.GetCurrent().Caller.AssertHasMasterKey();

        Guard.Argument(newSettings, nameof(newSettings)).NotNull();
        Guard.Argument(newSettings.Settings, nameof(newSettings.Settings)).NotNull();
        _configStorage.Upsert(OwnerAppSettings.ConfigKey, newSettings);
    }

    private async Task CreateSystemApps()
    {
        await CreateFeedApp();
    }

    private async Task CreateFeedApp()
    {
        //Feed app
        var request = new AppRegistrationRequest()
        {
            AppId = SystemAppConstants.FeedAppId,
            Name = "Feed",
            AuthorizedCircles = new List<Guid>(),
            CircleMemberPermissionGrant = null,
            Drives = new List<DriveGrantRequest>()
            {
                new()
                {
                    PermissionedDrive = new PermissionedDrive()
                    {
                        Drive = SystemDriveConstants.FeedDrive,
                        Permission = DrivePermission.ReadWrite
                    }
                }
            },
            PermissionSet = new PermissionSet(new List<int>()
            {
                PermissionKeys.ReadMyFollowers
            })
        };

        await _appRegistrationService.RegisterApp(request);

        //Create a CAT for the background process
        // 🐈🐈🐈
        var feedCat = await _appRegistrationService.RegisterClientRaw(request.AppId, $"{request.Name} Distribution Service");

        //TODO: encrypt CAT for storage
        _tenantSystemStorage.SingleKeyValueStorage.Upsert(SystemAppConstants.FeedAppId, feedCat);
    }

    //
    private async Task<bool> CreateCircleIfNotExists(CreateCircleRequest request)
    {
        var existingCircleDef = _cns.GetCircleDefinition(request.Id);
        if (null == existingCircleDef)
        {
            await _cns.CreateCircleDefinition(request);
            return true;
        }

        return false;
    }

    private async Task<bool> CreateDriveIfNotExists(CreateDriveRequest request)
    {
        var drive = await _driveManager.GetDriveIdByAlias(request.TargetDrive, false);

        if (null == drive)
        {
            await _driveManager.CreateDrive(request);
            return true;
        }

        return false;
    }
}