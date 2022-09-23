using System.Collections.Generic;
using System.Threading.Tasks;
using Youverse.Core.Services.Authorization.ExchangeGrants;
using Youverse.Core.Services.Authorization.Permissions;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Contacts.Circle;
using Youverse.Core.Services.Contacts.Circle.Membership;
using Youverse.Core.Services.Contacts.Circle.Membership.Definition;
using Youverse.Core.Services.Drive;

namespace Youverse.Core.Services.Provisioning;

public class TenantProvisioningService
{
    private readonly ICircleNetworkService _cns;
    private readonly IDriveService _driveService;
    private readonly DotYouContextAccessor _contextAccessor;

    public TenantProvisioningService(ICircleNetworkService cns, DotYouContextAccessor contextAccessor, IDriveService driveService)
    {
        _cns = cns;
        _contextAccessor = contextAccessor;
        _driveService = driveService;
    }

    /// <summary>
    /// Configures aspects of the owner's identity that require the master key
    /// </summary>
    public async Task EnsureInitialOwnerSetup()
    {
        _contextAccessor.GetCurrent().Caller.AssertHasMasterKey();
        await this.CreateSystemCircle();
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
}