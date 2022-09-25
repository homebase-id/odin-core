using System;
using System.Collections.Generic;
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

        await this.CreateSystemDrives();
        foreach (var rd in request.Drives)
        {
            await _driveService.CreateDrive(rd);
        }

        //best to create system circle after all drives are created so anon are provisioned correctly
        await this.CreateSystemCircle();
    }

    public async Task CreateSystemDrives()
    {
        var contactDrive = await _driveService.CreateDrive(SystemDriveConstants.CreateContactDriveRequest);
        var profileDrive = await _driveService.CreateDrive(SystemDriveConstants.CreateProfileDriveRequest);
    }

    private async Task CreateSystemCircle()
    {
        if (null == _cns.GetCircleDefinition(CircleConstants.SystemCircleId))
        {
            await _cns.CreateCircleDefinition(new CreateCircleRequest()
            {
                Id = CircleConstants.SystemCircleId.Value,
                Name = "System Circle",
                Description = "All Connected Identities",
                DriveGrants = new DriveGrantRequest[] { },
                Permissions = new PermissionSet()
                {
                    Keys = new List<int>() { PermissionKeys.ReadConnections }
                }
            });
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
                throw new YouverseException("Flag is valid but not handled");
        }

        _configStorage.Upsert(TenantSystemConfig.ConfigKey, cfg);
    }

    public TenantSystemConfig GetTenantSystemConfig()
    {
        return _configStorage.Get<TenantSystemConfig>(TenantSystemConfig.ConfigKey);
    }
}