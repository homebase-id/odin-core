using System;
using System.Threading.Tasks;
using Odin.Services.Drives;
using Odin.Hosting.Tests._Universal.ApiClient.Factory;
using Odin.Hosting.Tests._Universal.ApiClient.Owner;
using Odin.Hosting.Tests.OwnerApi.ApiClient;

namespace Odin.Hosting.Tests._Universal;

public interface IApiClientContext
{
    // Create the app and setup permissions
    // Create the guest domain and setup permissions
    Task Initialize(OwnerApiClientRedux ownerApiClient);
    
    TargetDrive TargetDrive { get; }
    
    DrivePermission DrivePermission { get; }

    IApiClientFactory GetFactory();
}