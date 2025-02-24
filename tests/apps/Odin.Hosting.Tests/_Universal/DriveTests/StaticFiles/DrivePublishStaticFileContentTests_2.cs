using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Mime;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http.HttpResults;
using NUnit.Framework;
using NUnit.Framework.Legacy;
using Odin.Core;
using Odin.Core.Serialization;
using Odin.Services.Authorization.Acl;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Query;
using Odin.Services.Drives.FileSystem.Base.Upload;
using Odin.Services.Optimization.Cdn;
using Odin.Hosting.Controllers.Base.Cdn;
using Odin.Hosting.Controllers.OwnerToken.Cdn;
using Odin.Hosting.Tests._Universal.ApiClient.Drive;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Drive;

namespace Odin.Hosting.Tests._Universal.DriveTests.StaticFiles
{
    public class DrivePublishStaticFileContentTests_2
    {
        private WebScaffold _scaffold;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            var folder = GetType().Name;
            _scaffold = new WebScaffold(folder);
            _scaffold.RunBeforeAnyTests();
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            _scaffold.RunAfterAnyTests();
        }

        [SetUp]
        public void Setup()
        {
            _scaffold.ClearAssertLogEventsAction();
            _scaffold.ClearLogEvents();
        }

        [TearDown]
        public void TearDown()
        {
            _scaffold.AssertLogEvents();
        }


        public static IEnumerable TestCases()
        {
            yield return new object[] { new GuestWriteOnlyAccessToDrive(TargetDrive.NewTargetDrive()), HttpStatusCode.MethodNotAllowed };
            yield return new object[] { new AppWriteOnlyAccessToDrive(TargetDrive.NewTargetDrive()), HttpStatusCode.MethodNotAllowed };
            yield return new object[] { new OwnerClientContext(TargetDrive.NewTargetDrive()), HttpStatusCode.NoContent };
        }

        [Test]
        [TestCaseSource(nameof(TestCases))]
        public async Task CanPublishPublicProfileCard(IApiClientContext callerContext, HttpStatusCode expectedStatusCode)
        {
            var identity = TestIdentities.Frodo;
            var client = _scaffold.CreateOwnerApiClientRedux(identity);
            await client.DriveManager.CreateDrive(callerContext.TargetDrive, "Some Drive", "", false, false, false);
            
            await callerContext.Initialize(client);
            var staticFileClient = new UniversalStaticFileApiClient(identity.OdinId, callerContext.GetFactory());
            
            string expectedJson = "{name:'Sam'}";
            var response = await staticFileClient.PublishPublicProfileCard(new PublishPublicProfileCardRequest()
            {
                ProfileCardJson = expectedJson
            });

            ClassicAssert.IsTrue(response.StatusCode == expectedStatusCode, $"Actual status code was {response.StatusCode}");

            if (expectedStatusCode == HttpStatusCode.OK)
            {
                var getProfileCardResponse2 = await client.StaticFilePublisher.GetPublicProfileCard();
                ClassicAssert.IsTrue(getProfileCardResponse2.IsSuccessStatusCode);
                ClassicAssert.IsNotNull(getProfileCardResponse2.Content);
                ClassicAssert.IsTrue(getProfileCardResponse2!.ContentHeaders!.ContentType!.MediaType == MediaTypeNames.Application.Json);
                var json = await getProfileCardResponse2.Content.ReadAsStringAsync();
                ClassicAssert.IsNotNull(json);
                ClassicAssert.IsTrue(json == expectedJson);
            }
        }

        [Test]
        [TestCaseSource(nameof(TestCases))]
        public async Task CanPublishPublicProfileImage(IApiClientContext callerContext, HttpStatusCode expectedStatusCode)
        {
            var identity = TestIdentities.Frodo;
            var client = _scaffold.CreateOwnerApiClientRedux(identity);
            await client.DriveManager.CreateDrive(callerContext.TargetDrive, "Some Drive", "", false, false, false);
            
            await callerContext.Initialize(client);
            var staticFileClient = new UniversalStaticFileApiClient(identity.OdinId, callerContext.GetFactory());
            

            var expectedImage = TestMedia.ThumbnailBytes300;
            var response = await staticFileClient.PublishPublicProfileImage(new PublishPublicProfileImageRequest()
            {
                Image64 = expectedImage.ToBase64(),
                ContentType = MediaTypeNames.Image.Jpeg
            });

            ClassicAssert.IsTrue(response.StatusCode == expectedStatusCode, $"Actual status code was {response.StatusCode}");

            if (expectedStatusCode == HttpStatusCode.OK)
            {
                var getPublicProfileImage2 = await client.StaticFilePublisher.GetPublicProfileImage();
                ClassicAssert.IsTrue(getPublicProfileImage2.IsSuccessStatusCode);
                ClassicAssert.IsNotNull(getPublicProfileImage2.Content);
                ClassicAssert.IsTrue(getPublicProfileImage2.ContentHeaders.ContentType.MediaType == MediaTypeNames.Image.Jpeg);
                var bytes = await getPublicProfileImage2.Content.ReadAsByteArrayAsync();
                ClassicAssert.IsNotNull(ByteArrayUtil.EquiByteArrayCompare(expectedImage, bytes));
            }
        }
    }
}