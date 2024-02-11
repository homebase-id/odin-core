using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Odin.Core.Exceptions;
using Odin.Core.Services.Apps;
using Odin.Core.Services.Authentication.Owner;
using Odin.Core.Services.Authorization.Apps;
using Odin.Core.Services.Authorization.ExchangeGrants;
using Odin.Core.Services.Authorization.Permissions;
using Odin.Core.Services.Base;
using Odin.Core.Services.Configuration.Eula;
using Odin.Core.Services.Drives;
using Odin.Core.Services.Drives.Management;
using Odin.Core.Services.EncryptionKeyService;
using Odin.Core.Services.Membership.CircleMembership;
using Odin.Core.Services.Membership.Circles;
using Odin.Core.Services.Membership.Connections;
using Odin.Core.Services.Registry;
using Odin.Core.Services.Util;
using Odin.Core.Storage;
using Odin.Core.Time;

namespace Odin.Core.Services.Configuration;

/// <summary>
/// Manages initial setup and system configuration for the identity and owner-app
/// </summary>
public class TenantConfigService
{
    private readonly CircleNetworkService _cns;
    private readonly OdinContextAccessor _contextAccessor;
    private readonly TenantContext _tenantContext;
    private readonly SingleKeyValueStorage _configStorage;
    private readonly IIdentityRegistry _registry;
    private readonly DriveManager _driveManager;
    private readonly PublicPrivateKeyService _publicPrivateKeyService;
    private readonly RecoveryService _recoverService;
    private readonly IcrKeyService _icrKeyService;
    private readonly CircleMembershipService _circleMembershipService;
    private readonly IAppRegistrationService _appRegistrationService;

    public TenantConfigService(CircleNetworkService cns, OdinContextAccessor contextAccessor,
        TenantSystemStorage storage, TenantContext tenantContext,
        IIdentityRegistry registry,
        DriveManager driveManager,
        PublicPrivateKeyService publicPrivateKeyService,
        IcrKeyService icrKeyService,
        RecoveryService recoverService,
        CircleMembershipService circleMembershipService,
        IAppRegistrationService appRegistrationService)
    {
        _cns = cns;
        _contextAccessor = contextAccessor;
        _tenantContext = tenantContext;
        _registry = registry;
        _driveManager = driveManager;
        _publicPrivateKeyService = publicPrivateKeyService;
        _recoverService = recoverService;
        _circleMembershipService = circleMembershipService;
        _appRegistrationService = appRegistrationService;
        _icrKeyService = icrKeyService;

        const string configContextKey = "b9e1c2a3-e0e0-480e-a696-ce602b052d07";
        _configStorage = storage.CreateSingleKeyValueStorage(Guid.Parse(configContextKey));

        _tenantContext.UpdateSystemConfig(this.GetTenantSettings());
    }

    public bool IsIdentityServerConfigured()
    {
        //ok for anonymous to query this as long as we're only returning a bool
        var firstRunInfo = _configStorage.Get<FirstRunInfo>(FirstRunInfo.Key);
        return firstRunInfo != null;
    }

    public bool IsEulaSignatureRequired()
    {
        _contextAccessor.GetCurrent().Caller.AssertHasMasterKey();

        var info = _configStorage.Get<List<EulaSignature>>(EulaSystemInfo.StorageKey);
        if (info == null || !info.Any())
        {
            return true;
        }

        var signature = info.SingleOrDefault(signature => signature.Version == EulaSystemInfo.RequiredVersion);
        return signature == null;
    }

    public EulaVersionResponse GetRequiredEulaVersion()
    {
        _contextAccessor.GetCurrent().Caller.AssertHasMasterKey();
        return new EulaVersionResponse()
        {
            Version = EulaSystemInfo.RequiredVersion
        };
    }

    public List<EulaSignature> GetEulaSignatureHistory()
    {
        _contextAccessor.GetCurrent().Caller.AssertHasMasterKey();

        var signatures = _configStorage.Get<List<EulaSignature>>(EulaSystemInfo.StorageKey) ?? new List<EulaSignature>();

        return signatures;
    }

    public void MarkEulaSigned(MarkEulaSignedRequest request)
    {
        _contextAccessor.GetCurrent().Caller.AssertHasMasterKey();

        OdinValidationUtils.AssertNotNull(request, nameof(request));
        OdinValidationUtils.AssertNotNullOrEmpty(request.Version, nameof(request.Version));

        if (request.Version != EulaSystemInfo.RequiredVersion)
        {
            throw new OdinClientException("Invalid Eula version");
        }

        var signatures = _configStorage.Get<List<EulaSignature>>(EulaSystemInfo.StorageKey) ?? new List<EulaSignature>();

        signatures.Add(new EulaSignature()
        {
            SignatureDate = UnixTimeUtc.Now(),
            Version = request.Version,
            SignatureBytes = request.SignatureBytes
        });

        _configStorage.Upsert(Eula.EulaSystemInfo.StorageKey, signatures);
    }

    public async Task CreateInitialKeys()
    {
        _contextAccessor.GetCurrent().Caller.AssertHasMasterKey();

        await _recoverService.CreateInitialKey();

        await _publicPrivateKeyService.CreateInitialKeys();

        await _icrKeyService.CreateInitialKeys();
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
        await _circleMembershipService.CreateSystemCircle();

        await CreateDriveIfNotExists(SystemDriveConstants.CreateChatDriveRequest);
        await CreateDriveIfNotExists(SystemDriveConstants.CreateFeedDriveRequest);
        await CreateDriveIfNotExists(SystemDriveConstants.CreateHomePageConfigDriveRequest);
        await CreateDriveIfNotExists(SystemDriveConstants.CreatePublicPostsChannelDriveRequest);

        await CreateDriveIfNotExists(SystemDriveConstants.CreateContactDriveRequest);
        await CreateDriveIfNotExists(SystemDriveConstants.CreateProfileDriveRequest);
        await CreateDriveIfNotExists(SystemDriveConstants.CreateWalletDriveRequest);
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

        await this.RegisterBuiltInApps();

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
            throw new OdinClientException("Invalid flag name", OdinClientErrorCode.InvalidFlagName);
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

            default:
                throw new OdinClientException("Flag name is valid but not handled",
                    OdinClientErrorCode.UnknownFlagName);
        }

        _configStorage.Upsert(TenantSettings.ConfigKey, cfg);

        //TODO: eww, use mediator instead
        _tenantContext.UpdateSystemConfig(cfg);
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
        _configStorage.Upsert(OwnerAppSettings.ConfigKey, newSettings);
    }

    //

    private async Task RegisterBuiltInApps()
    {
        await RegisterChatApp();
        await RegisterFeedApp();
        // await RegisterPhotosApp();
    }

    private async Task RegisterFeedApp()
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
                        Permission = DrivePermission.Read
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

        await _appRegistrationService.RegisterApp(request);
    }


    private async Task RegisterChatApp()
    {
        var request = new AppRegistrationRequest()
        {
            AppId = SystemAppConstants.ChatAppId,
            Name = "Homebase - Chat",
            AuthorizedCircles = new List<Guid>() //note: by default the system circle will have write access to chat drive
            {
                SystemCircleConstants.ConnectedIdentitiesSystemCircleId
            },
            CircleMemberPermissionGrant = new PermissionSetGrantRequest()
            {
                Drives =
                [
                    new()
                    {
                        PermissionedDrive = new PermissionedDrive()
                        {
                            Drive = SystemDriveConstants.ChatDrive,
                            Permission = DrivePermission.Write
                        }
                    }
                ],
                PermissionSet = new PermissionSet()

                // PermissionSet = new PermissionSet(
                //     PermissionKeys.ReadConnections,
                //     PermissionKeys.SendPushNotifications,
                //     PermissionKeys.UseTransitRead,
                //     PermissionKeys.UseTransitWrite)
            },
            Drives =
            [
                new()
                {
                    PermissionedDrive = new PermissionedDrive()
                    {
                        Drive = SystemDriveConstants.ChatDrive,
                        Permission = DrivePermission.ReadWrite
                    }
                },
                new()
                {
                    PermissionedDrive = new PermissionedDrive()
                    {
                        Drive = SystemDriveConstants.ContactDrive,
                        Permission = DrivePermission.Read
                    }
                },
                new()
                {
                    PermissionedDrive = new PermissionedDrive()
                    {
                        Drive = SystemDriveConstants.ProfileDrive,
                        Permission = DrivePermission.Read
                    }
                }
            ],
            PermissionSet = new PermissionSet(
                PermissionKeys.ReadConnections,
                PermissionKeys.SendPushNotifications,
                // PermissionKeys.UseTransitRead,
                PermissionKeys.UseTransitWrite)
        };

        await _appRegistrationService.RegisterApp(request);
    }

    private async Task<bool> CreateCircleIfNotExists(CreateCircleRequest request)
    {
        var existingCircleDef = _circleMembershipService.GetCircle(request.Id);
        if (null == existingCircleDef)
        {
            await _circleMembershipService.CreateCircleDefinition(request);
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

    private void UpdateSystemCirclePermission(int key, bool shouldGrantKey)
    {
        var systemCircle = _circleMembershipService.GetCircle(SystemCircleConstants.ConnectedIdentitiesSystemCircleId);
        
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

        _cns.UpdateCircleDefinition(systemCircle).GetAwaiter().GetResult();
    }
}