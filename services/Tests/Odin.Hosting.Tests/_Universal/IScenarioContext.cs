using System;
using System.Threading.Tasks;
using System.Xml.Schema;
using Odin.Core.Services.Drives;
using Odin.Hosting.Tests._Universal.ApiClient.Factory;
using Odin.Hosting.Tests.OwnerApi.ApiClient;

namespace Odin.Hosting.Tests._Universal;

public interface IScenarioContext
{
    // Create the app and setup permissions
    // Create the guest domain and setup permissions
    Task Initialize(OwnerApiClient ownerApiClient, TargetDrive targetDrive);

    IApiClientFactory GetFactory();
}

public static class ScenarioUtil
{
    public static IScenarioContext Instantiate(Type scenarioType)
    {
        var scenario = (IScenarioContext)Activator.CreateInstance(scenarioType);
        return scenario;
    }
}