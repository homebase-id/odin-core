using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Odin.Core;
using Odin.Hosting.Controllers.Base;
using Odin.Hosting.Controllers.ClientToken.Shared.Drive;
using Odin.Hosting.UnifiedV2.Authentication.Policy;
using Odin.Services.Base.SharedTypes;
using Odin.Services.Drives;
using Odin.Services.Drives.Management;
using Swashbuckle.AspNetCore.Annotations;

namespace Odin.Hosting.UnifiedV2.Drive;

[ApiController]
[Route(UnifiedApiRouteConstants.DrivesRoot)]
[UnifiedV2Authorize(UnifiedPolicies.OwnerOrApp)]
[ApiExplorerSettings(GroupName = "v2")]
public class V2DriveMetadataController(
    IDriveManager driveManager)
    : OdinControllerBase
{
    [HttpGet("metadata/channel-drives")]
    [SwaggerOperation(Tags = [SwaggerInfo.DriveMetadata])]
    public async Task<PagedResult<ClientDriveData>> GetChannelDrives()
    {
        var drives = await driveManager.GetDrivesAsync(SystemDriveConstants.ChannelDriveType,
            new PageOptions(1, 1000),
            WebOdinContext);

        var clientDriveData = drives.Results.Select(drive => new ClientDriveData()
        {
            TargetDrive = drive.TargetDriveInfo,
            Name = drive.Name,
            Attributes = drive.Attributes
        }).ToList();

        return new PagedResult<ClientDriveData>(drives.Request, drives.TotalPages, clientDriveData);
    }
}