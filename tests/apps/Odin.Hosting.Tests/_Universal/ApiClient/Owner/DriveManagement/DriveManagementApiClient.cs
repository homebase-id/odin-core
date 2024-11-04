using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Odin.Core;
using Odin.Services.Drives;
using Odin.Services.Drives.Management;
using Odin.Hosting.Controllers.OwnerToken.Drive;
using Odin.Hosting.Tests.OwnerApi.Utils;
using Refit;

namespace Odin.Hosting.Tests._Universal.ApiClient.Owner.DriveManagement;

public class DriveManagementApiClient
{
    private readonly TestIdentity _identity;
    private readonly OwnerApiTestUtils _ownerApi;

    public DriveManagementApiClient(OwnerApiTestUtils ownerApi, TestIdentity identity)
    {
        _ownerApi = ownerApi;
        _identity = identity;
    }

    public async Task<ApiResponse<bool>> CreateDrive(TargetDrive targetDrive, string name, string metadata, bool allowAnonymousReads, bool ownerOnly = false,
        bool allowSubscriptions = false, Dictionary<string, string> attributes = null)
    {
        var client = this._ownerApi.CreateOwnerApiHttpClient(_identity, out var ownerSharedSecret);
        {
            var svc = RefitCreator.RestServiceFor<IRefitDriveManagement>(client, ownerSharedSecret);

            if (ownerOnly && allowAnonymousReads)
            {
                throw new Exception("cannot have an owner only drive that allows anonymous reads");
            }

            var response = await svc.CreateDrive(new CreateDriveRequest()
            {
                TargetDrive = targetDrive,
                Name = name,
                Metadata = metadata,
                AllowAnonymousReads = allowAnonymousReads,
                AllowSubscriptions = allowSubscriptions,
                OwnerOnly = ownerOnly,
                Attributes = attributes
            });

            return response;
        }
    }

    public async Task<ApiResponse<PagedResult<OwnerClientDriveData>>> GetDrives(int pageNumber = 1, int pageSize = 100)
    {
        var client = _ownerApi.CreateOwnerApiHttpClient(_identity, out var sharedSecret);

        var driveSvc = RefitCreator.RestServiceFor<IRefitDriveManagement>(client, sharedSecret);
        return await driveSvc.GetDrives(new GetDrivesRequest() { PageNumber = pageNumber, PageSize = pageSize });
    }
}