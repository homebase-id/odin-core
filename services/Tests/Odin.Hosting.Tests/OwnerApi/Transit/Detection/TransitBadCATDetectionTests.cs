using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using NUnit.Framework;
using Odin.Core;
using Odin.Core.Serialization;
using Odin.Core.Services.Authorization.Acl;
using Odin.Core.Services.Authorization.ExchangeGrants;
using Odin.Core.Services.Base;
using Odin.Core.Services.DataSubscription.Follower;
using Odin.Core.Services.Drives;
using Odin.Core.Services.Drives.DriveCore.Query;
using Odin.Core.Services.Drives.DriveCore.Storage;
using Odin.Core.Services.Drives.FileSystem.Base.Upload;
using Odin.Core.Services.Peer;
using Odin.Core.Services.Peer.Encryption;
using Odin.Core.Storage;
using Odin.Core.Time;
using Odin.Hosting.Tests.AppAPI.Utils;
using Odin.Hosting.Tests.OwnerApi.ApiClient;

namespace Odin.Hosting.Tests.OwnerApi.Transit.Detection;

[TestFixture]
public class TransitBadCATDetectionTests
{
    private WebScaffold _scaffold;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        string folder = MethodBase.GetCurrentMethod()!.DeclaringType!.Name;
        _scaffold = new WebScaffold(folder);
        _scaffold.RunBeforeAnyTests();
    }

    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        _scaffold.RunAfterAnyTests();
    }
    


    [Test]
    public async Task CanDetectBadCAT_and_UpdateICR()
    {
        // Prepare Scenario

        // 1. Connect the hobbits
        var targetDrive = TargetDrive.NewTargetDrive();
        var scenarioCtx = await _scaffold.Scenarios.CreateConnectedHobbits(targetDrive);

        // 2. Merry posts content
        //create public drive
        //upload content
        var merryOwnerClient = _scaffold.CreateOwnerApiClient(TestIdentities.Merry);
        var merryPublicDrive = TargetDrive.NewTargetDrive();
        await merryOwnerClient.Drive.CreateDrive(merryPublicDrive, "a public blog", "", true, false, true);

        const string headerContent = "some header content";
        const string payloadContent = "this is the payload";

        var fileMetadata = new UploadFileMetadata()
        {
            AllowDistribution = true,
            ContentType = "plain/text",
            AppData = new UploadAppFileMetaData()
            {
                FileType = 10101,
                JsonContent = headerContent,
                ContentIsComplete = false
            },
            PayloadIsEncrypted = false,
            AccessControlList = AccessControlList.Anonymous
        };

        var publicFile = await merryOwnerClient.Drive.UploadFile(FileSystemType.Standard, merryPublicDrive, fileMetadata, payloadContent);

        //
        // 3. Pippin can read it via transit query service
        //
        var pippinOwnerClient = _scaffold.CreateOwnerApiClient(TestIdentities.Pippin);
        
        var getTransitPayloadResponse1 = await pippinOwnerClient.Transit.GetPayloadOverTransit(merryOwnerClient.Identity.OdinId, publicFile.File);
        Assert.That(getTransitPayloadResponse1.IsSuccessStatusCode, Is.True);
        Assert.That(getTransitPayloadResponse1.Content, Is.Not.Null);

        var remotePayload = await getTransitPayloadResponse1.Content.ReadAsStringAsync();
        Assert.IsTrue(remotePayload == payloadContent);

        //
        // Merry gets mad and disconnects from Pippin, pippin leaves for Gondor with Gandalf 
        //
        await merryOwnerClient.Network.DisconnectFrom(pippinOwnerClient.Identity);
        
        //
        // Pippin makes transit query call to Merry still thinking they are connected (therefore he sends CAT)
        //
        var getTransitPayloadResponse2 = await pippinOwnerClient.Transit.GetPayloadOverTransit(merryOwnerClient.Identity.OdinId, publicFile.File);

        // Assert.That(getTransitPayloadResponse2.IsSuccessStatusCode, Is.True);
        // Assert.That(getTransitPayloadResponse2.Content, Is.Not.Null);
        //
        // var remotePayload2 = await getTransitPayloadResponse2.Content.ReadAsStringAsync();
        // Assert.IsTrue(remotePayload2 == payloadContent);

        Assert.IsTrue(getTransitPayloadResponse2.StatusCode == HttpStatusCode.Forbidden, $"Status code was {getTransitPayloadResponse2.StatusCode}");

        
        // Merry's server detects bad CAT, rejects the call and Tells Pippin's server that the CAT is invalid

        // Pippin's server sees bad CAT then and updates Merry's ICR with a flag indicating not to use the CAT

        //here we need to change the transit query service to not use the CAT when it's marked bad

        // Pippin makes a request w/o sending the CAT and thus access is Public
        //Note: we have the option of just falling back to public access in transit when you send a bad CAT
    }
}