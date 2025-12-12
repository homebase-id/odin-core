using System.Threading.Tasks;
using Odin.Hosting.Tests._Universal.ApiClient.Factory;
using Odin.Hosting.Tests._Universal.ApiClient.Owner;
using Odin.Services.Drives;

namespace Odin.Hosting.Tests._V2.ApiClient;

public interface IApiClientContextV2
{
    // Create the app and setup permissions
    // Create the guest domain and setup permissions
    Task Initialize(OwnerApiClientRedux ownerApiClient);
    
    TargetDrive TargetDrive { get; }
    
    DrivePermission DrivePermission { get; }

    IApiClientFactory GetFactory();

    /// <summary>
    /// Remove anything related this factory (i.e. delete youauth domain registrations, etc).
    /// </summary>
    /// <returns></returns>
    Task Cleanup();
}