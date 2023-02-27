using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Youverse.Core;
using Youverse.Core.Identity;
using Youverse.Core.Services.Authorization.ExchangeGrants;
using Youverse.Core.Services.Contacts.Circle.Membership.Definition;
using Youverse.Core.Services.Drive;
using Youverse.Hosting.Tests.OwnerApi.Utils;

namespace Youverse.Hosting.Tests.AppAPI.Utils;

/// <summary>
/// Creates various apps, identities, and connections for scenarios
/// </summary>
public class ScenarioBootstrapper
{
    private readonly OwnerApiTestUtils _ownerApi;
    private readonly AppApiTestUtils _appApi;

    public ScenarioBootstrapper(OwnerApiTestUtils ownerApi, AppApiTestUtils appApi)
    {
        _ownerApi = ownerApi;
        _appApi = appApi;
    }

    public async Task<ScenarioContext> CreateConnectedHobbits(TargetDrive targetDrive)
    {
        var scenarioContext = new ScenarioContext()
        {
            AppId = Guid.NewGuid(),
            AppContexts = new Dictionary<OdinId, TestAppContext>()
        };

        var frodoAppContext = await _ownerApi.SetupTestSampleApp(scenarioContext.AppId, TestIdentities.Frodo, canReadConnections: true, targetDrive, driveAllowAnonymousReads: false);
        var frodoCircleDef = await CreateCircle(frodoAppContext.Identity, targetDrive);
        scenarioContext.AppContexts.Add(frodoAppContext.Identity, frodoAppContext);
        
        var merryAppContext = await _ownerApi.SetupTestSampleApp(scenarioContext.AppId, TestIdentities.Merry, canReadConnections: true, targetDrive, driveAllowAnonymousReads: false);
        var merryCircleDef = await CreateCircle(merryAppContext.Identity, targetDrive);
        scenarioContext.AppContexts.Add(merryAppContext.Identity, merryAppContext);
        
        var pippinAppContext = await _ownerApi.SetupTestSampleApp(scenarioContext.AppId, TestIdentities.Pippin, canReadConnections: true, targetDrive, driveAllowAnonymousReads: false);
        var pippinCircleDef = await CreateCircle(pippinAppContext.Identity, targetDrive);
        scenarioContext.AppContexts.Add(pippinAppContext.Identity, pippinAppContext);

        var samAppContext = await _ownerApi.SetupTestSampleApp(scenarioContext.AppId, TestIdentities.Samwise, canReadConnections: true, targetDrive, driveAllowAnonymousReads: false);
        var samCircleDef = await CreateCircle(samAppContext.Identity, targetDrive);
        scenarioContext.AppContexts.Add(samAppContext.Identity, samAppContext);
        

        await _ownerApi.CreateConnection(TestIdentities.Frodo.OdinId, TestIdentities.Samwise.OdinId, new CreateConnectionOptions()
        {
            CircleIdsGrantedToRecipient = new List<GuidId>() { frodoCircleDef.Id },
            CircleIdsGrantedToSender = new List<GuidId>() { samCircleDef.Id }
        });

        await _ownerApi.CreateConnection(TestIdentities.Frodo.OdinId, TestIdentities.Merry.OdinId, new CreateConnectionOptions()
        {
            CircleIdsGrantedToRecipient = new List<GuidId>() { frodoCircleDef.Id },
            CircleIdsGrantedToSender = new List<GuidId>() { merryCircleDef.Id }
        });

        await _ownerApi.CreateConnection(TestIdentities.Frodo.OdinId, TestIdentities.Pippin.OdinId, new CreateConnectionOptions()
        {
            CircleIdsGrantedToRecipient = new List<GuidId>() { frodoCircleDef.Id },
            CircleIdsGrantedToSender = new List<GuidId>() { pippinCircleDef.Id }
        });

        await _ownerApi.CreateConnection(TestIdentities.Samwise.OdinId, TestIdentities.Merry.OdinId, new CreateConnectionOptions()
        {
            CircleIdsGrantedToRecipient = new List<GuidId>() { samCircleDef.Id },
            CircleIdsGrantedToSender = new List<GuidId>() { merryCircleDef.Id }
        });

        await _ownerApi.CreateConnection(TestIdentities.Samwise.OdinId, TestIdentities.Pippin.OdinId, new CreateConnectionOptions()
        {
            CircleIdsGrantedToRecipient = new List<GuidId>() { samCircleDef.Id },
            CircleIdsGrantedToSender = new List<GuidId>() { pippinCircleDef.Id }
        });

        await _ownerApi.CreateConnection(TestIdentities.Pippin.OdinId, TestIdentities.Merry.OdinId, new CreateConnectionOptions()
        {
            CircleIdsGrantedToRecipient = new List<GuidId>() { pippinCircleDef.Id },
            CircleIdsGrantedToSender = new List<GuidId>() { merryCircleDef.Id }
        });

        return scenarioContext;
    }

    private async Task<CircleDefinition> CreateCircle(OdinId identity, TargetDrive targetDrive)
    {
        return await _ownerApi.CreateCircleWithDrive(identity, $"Sender ({identity}) Circle",
            permissionKeys: new List<int>() { },
            drive: new PermissionedDrive()
            {
                Drive = targetDrive,
                Permission = DrivePermission.ReadWrite
            });
    }
}

public class ScenarioContext
{
    public Guid AppId { get; set; }

    public Dictionary<OdinId, TestAppContext> AppContexts { get; set; }
}