using System;
using System.Threading.Tasks;
using Odin.Core.Services.Drives;
using Odin.Hosting.Tests._Universal.ApiClient.Factory;
using Odin.Hosting.Tests.OwnerApi.ApiClient;

namespace Odin.Hosting.Tests._Universal;

public interface IApiClientContext
{
    // Create the app and setup permissions
    // Create the guest domain and setup permissions
    // Task Initialize(OwnerApiClient ownerApiClient, TargetDrive targetDrive);
    Task Initialize(OwnerApiClient ownerApiClient);
    
    TargetDrive TargetDrive { get; }

    IApiClientFactory GetFactory();
}

public static class ScenarioUtil
{
    public static IApiClientContext Instantiate(Type scenarioType, TestPermissionKeyList testPermissionKeyList)
    {
        var scenario = (IApiClientContext)Activator.CreateInstance(scenarioType, testPermissionKeyList);
        return scenario;
    }
}