using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Youverse.Core.Exceptions;
using Youverse.Core.Services.Authorization.ExchangeGrants;
using Youverse.Core.Services.Authorization.Permissions;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Contacts.Circle;
using Youverse.Core.Services.Contacts.Circle.Membership;
using Youverse.Core.Services.Contacts.Circle.Membership.Definition;
using Youverse.Core.Services.Drive;
using Youverse.Core.Storage;

namespace Youverse.Core.Services.Provisioning;

/// <summary>
/// Manages initial setup and system configuration for the identity and owner-app
/// </summary>
public class TenantConfigService
{
    private readonly ICircleNetworkService _cns;
    private readonly IDriveService _driveService;
    private readonly DotYouContextAccessor _contextAccessor;

    private readonly SingleKeyValueStorage _configStorage;

    public TenantConfigService(ICircleNetworkService cns, DotYouContextAccessor contextAccessor, IDriveService driveService, ISystemStorage storage)
    {
        _cns = cns;
        _contextAccessor = contextAccessor;
        _driveService = driveService;
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
         await   CreateCircleIfNotExists(rc);
        }
    }

    public void UpdateSystemFlag(UpdateFlagRequest request)
    {
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

                // update system circle
                break;
            default:
                throw new YouverseException("Flag name is valid but not handled");
        }

        _configStorage.Upsert(TenantSystemConfig.ConfigKey, cfg);
    }

    public TenantSystemConfig GetTenantSystemConfig()
    {
        return _configStorage.Get<TenantSystemConfig>(TenantSystemConfig.ConfigKey) ?? TenantSystemConfig.Default;
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