using System;
using System.Collections.Generic;
using System.Configuration;
using System.Threading.Tasks;
using Dawn;
using Youverse.Core.Exceptions;
using Youverse.Core.Services.Authorization.Permissions;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Contacts.Circle;
using Youverse.Core.Services.Contacts.Circle.Membership;
using Youverse.Core.Services.Contacts.Circle.Membership.Definition;
using Youverse.Core.Services.Drive;
using Youverse.Core.Storage;

namespace Youverse.Core.Services.Configuration;

/// <summary>
/// Manages initial setup and system configuration for the identity and owner-app
/// </summary>
public class TenantConfigService
{
    private readonly ICircleNetworkService _cns;
    private readonly IDriveService _driveService;
    private readonly DotYouContextAccessor _contextAccessor;
    private readonly TenantContext _tenantContext;
    private readonly SingleKeyValueStorage _configStorage;

    public TenantConfigService(ICircleNetworkService cns, DotYouContextAccessor contextAccessor, IDriveService driveService, ISystemStorage storage, TenantContext tenantContext)
    {
        _cns = cns;
        _contextAccessor = contextAccessor;
        _driveService = driveService;
        _tenantContext = tenantContext;
        _configStorage = storage.SingleKeyValueStorage;
    }


    /// <summary>
    /// Configures aspects of the owner's identity that require the master key
    /// </summary>
    public async Task EnsureInitialOwnerSetup(InitialSetupRequest request)
    {
        _contextAccessor.GetCurrent().Caller.AssertHasMasterKey();

        //Note: the order here is important.  if the request includes any anonymous
        //drives, they should be added after the system circle exists

        await _cns.CreateSystemCircle();

        await CreateDriveIfNotExists(SystemDriveConstants.CreateContactDriveRequest);
        await CreateDriveIfNotExists(SystemDriveConstants.CreateProfileDriveRequest);


        foreach (var rd in request.Drives ?? new List<CreateDriveRequest>())
        {
            await CreateDriveIfNotExists(rd);
        }

        //Create additional circles last in case they rely on any of the drives above
        foreach (var rc in request.Circles ?? new List<CreateCircleRequest>())
        {
            await CreateCircleIfNotExists(rc);
        }
    }

    public void UpdateSystemFlag(UpdateFlagRequest request)
    {
        _contextAccessor.GetCurrent().Caller.AssertHasMasterKey();

        if (!Enum.TryParse(typeof(TenantConfigFlagNames), request.FlagName, true, out var flag))
        {
            throw new YouverseException("Invalid flag name");
        }

        var cfg = _configStorage.Get<TenantSystemConfig>(TenantSystemConfig.ConfigKey) ?? new TenantSystemConfig();

        switch (flag)
        {
            case TenantConfigFlagNames.AnonymousVisitorsCanViewConnections:
                cfg.AnonymousVisitorsCanViewConnections = bool.Parse(request.Value);
                break;

            case TenantConfigFlagNames.AuthenticatedIdentitiesCanViewConnections:
                cfg.AuthenticatedIdentitiesCanViewConnections = bool.Parse(request.Value);
                break;

            case TenantConfigFlagNames.ConnectedIdentitiesCanViewConnections:
                cfg.AllConnectedIdentitiesCanViewConnections = bool.Parse(request.Value);
                this.UpdateSystemCircle(cfg.AllConnectedIdentitiesCanViewConnections);
                break;

            default:
                throw new YouverseException("Flag name is valid but not handled");
        }

        _configStorage.Upsert(TenantSystemConfig.ConfigKey, cfg);

        //TODO: eww, use mediator instead
        _tenantContext.UpdateSystemConfig(cfg);
    }

    private void UpdateSystemCircle(bool canReadConnections)
    {
        var systemCircle = _cns.GetCircleDefinition(CircleConstants.SystemCircleId);

        if (canReadConnections)
        {
            if (!systemCircle.Permissions.Keys.Contains(PermissionKeys.ReadConnections))
            {
                systemCircle.Permissions.Keys.Add(PermissionKeys.ReadConnections);
            }
        }
        else
        {
            if (systemCircle.Permissions.Keys.Contains(PermissionKeys.ReadConnections))
            {
                systemCircle.Permissions.Keys.Remove(PermissionKeys.ReadConnections);
            }
        }

        _cns.UpdateCircleDefinition(systemCircle);
    }

    public TenantSystemConfig GetTenantSystemConfig()
    {
        return _configStorage.Get<TenantSystemConfig>(TenantSystemConfig.ConfigKey) ?? TenantSystemConfig.Default;
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
        var drive = await _driveService.GetDriveIdByAlias(request.TargetDrive, false);

        if (null == drive)
        {
            await _driveService.CreateDrive(request);
            return true;
        }

        return false;
    }
}