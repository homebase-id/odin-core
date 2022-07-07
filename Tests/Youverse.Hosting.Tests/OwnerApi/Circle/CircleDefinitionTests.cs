using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using Refit;
using Youverse.Core;
using Youverse.Core.Services.Authorization.Permissions;
using Youverse.Core.Services.Contacts.Circle;
using Youverse.Core.Services.Contacts.Circle.Definition;
using Youverse.Core.Services.Contacts.Circle.Membership;
using Youverse.Core.Services.Contacts.Circle.Requests;
using Youverse.Core.Services.Drive;

namespace Youverse.Hosting.Tests.OwnerApi.Circle
{
    public class CircleDefinitionTests
    {
        private WebScaffold _scaffold;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            string folder = MethodBase.GetCurrentMethod().DeclaringType.Name;
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
            //runs before each test 
            //_scaffold.DeleteData(); 
        }


        [Test]
        public async Task CanCreateCircle()
        {
            using (var client = _scaffold.OwnerApi.CreateOwnerApiHttpClient(TestIdentities.Samwise))
            {
                var svc = RestService.For<ICircleDefinitionOwnerClient>(client);

                var request = new CreateCircleRequest()
                {
                    Name = "Test Circle",
                    Description = "Test circle description",
                    Drives = null,
                    Permissions = null
                };

                var createCircleResponse = await svc.Create(request);
                Assert.IsTrue(createCircleResponse.IsSuccessStatusCode, $"Failed.  Actual response {createCircleResponse.StatusCode}");

                var getCircleDefinitionsResponse = await svc.GetCircleDefinitions();
                Assert.IsTrue(getCircleDefinitionsResponse.IsSuccessStatusCode, $"Failed.  Actual response {getCircleDefinitionsResponse.StatusCode}");

                var definitionList = getCircleDefinitionsResponse.Content;
                Assert.IsNotNull(definitionList);

                var circle = definitionList.Single();

                Assert.AreEqual(request.Name, circle.Name);
                Assert.AreEqual(request.Description, circle.Description);
                CollectionAssert.AreEqual(request.Drives, circle.Drives);
                Assert.IsTrue(request.Permissions == circle.Permissions);
            }
        }

        [Test]
        public async Task CanGetListOfCircleDefinitions()
        {
            using (var client = _scaffold.OwnerApi.CreateOwnerApiHttpClient(TestIdentities.Samwise))
            {
                var svc = RestService.For<ICircleDefinitionOwnerClient>(client);

                var request1 = new CreateCircleRequest()
                {
                    Name = "Test Circle 1",
                    Description = "Test circle description 1",
                    Drives = null,
                    Permissions = null
                };

                var createCircleResponse1 = await svc.Create(request1);
                Assert.IsTrue(createCircleResponse1.IsSuccessStatusCode, $"Failed.  Actual response {createCircleResponse1.StatusCode}");


                var request2 = new CreateCircleRequest()
                {
                    Name = "Test Circle 2",
                    Description = "Test circle description 2",
                    Drives = null,
                    Permissions = null
                };

                var createCircleResponse2 = await svc.Create(request1);
                Assert.IsTrue(createCircleResponse2.IsSuccessStatusCode, $"Failed.  Actual response {createCircleResponse2.StatusCode}");

                var getCircleDefinitionsResponse = await svc.GetCircleDefinitions();
                Assert.IsTrue(getCircleDefinitionsResponse.IsSuccessStatusCode, $"Failed.  Actual response {getCircleDefinitionsResponse.StatusCode}");

                Assert.IsNotNull(getCircleDefinitionsResponse.Content);
                var definitionList = getCircleDefinitionsResponse.Content.ToList();
                
                Assert.IsTrue(definitionList.Count() == 2);

                var circle1 = definitionList[0];
                Assert.AreEqual(request1.Name, circle1.Name);
                Assert.AreEqual(request1.Description, circle1.Description);
                CollectionAssert.AreEqual(request1.Drives, circle1.Drives);
                Assert.IsTrue(request1.Permissions == circle1.Permissions);
                
                var circle2 = definitionList[1];
                Assert.AreEqual(request1.Name, circle2.Name);
                Assert.AreEqual(request1.Description, circle2.Description);
                CollectionAssert.AreEqual(request1.Drives, circle2.Drives);
                Assert.IsTrue(request1.Permissions == circle2.Permissions);
            }
        }

        [Test]
        public async Task CanUpdateCircle()
        {
            using (var client = _scaffold.OwnerApi.CreateOwnerApiHttpClient(TestIdentities.Samwise))
            {
                var svc = RestService.For<ICircleDefinitionOwnerClient>(client);

                var request = new CreateCircleRequest()
                {
                    Name = "Test Circle",
                    Description = "Test circle description",
                    Drives = null,
                    Permissions = null
                };

                var createCircleResponse = await svc.Create(request);
                Assert.IsTrue(createCircleResponse.IsSuccessStatusCode, $"Failed.  Actual response {createCircleResponse.StatusCode}");

                var getCircleDefinitionsResponse = await svc.GetCircleDefinitions();
                Assert.IsTrue(getCircleDefinitionsResponse.IsSuccessStatusCode, $"Failed.  Actual response {getCircleDefinitionsResponse.StatusCode}");

                var definitionList = getCircleDefinitionsResponse.Content;
                Assert.IsNotNull(definitionList);

                var circle = definitionList.Single();

                //

                circle.Name = "updated name";
                circle.Description = "updated description";

                circle.Drives = null;
                circle.Permissions = null;

                var updateCircleResponse = await svc.UpdateCircle(circle);
                Assert.IsTrue(updateCircleResponse.IsSuccessStatusCode, $"Actual response {updateCircleResponse.StatusCode}");

                var updatedDefinitionList = getCircleDefinitionsResponse.Content;
                Assert.IsNotNull(updatedDefinitionList);

                var circle2 = updatedDefinitionList.Single();

                Assert.AreEqual(circle.Name, circle2.Name);
                Assert.AreEqual(circle.Description, circle2.Description);
                CollectionAssert.AreEqual(circle.Drives, circle2.Drives);
                Assert.IsTrue(circle.Permissions == circle2.Permissions);

                //TODO: test that the changes to the drives and permissions were applied
            }
        }

        [Test]
        public async Task CanDeleteCircle()
        {
            using (var client = _scaffold.OwnerApi.CreateOwnerApiHttpClient(TestIdentities.Samwise))
            {
                var svc = RestService.For<ICircleDefinitionOwnerClient>(client);

                var request = new CreateCircleRequest()
                {
                    Name = "Test Circle",
                    Description = "Test circle description",
                    Drives = null,
                    Permissions = null
                };

                var createCircleResponse = await svc.Create(request);
                Assert.IsTrue(createCircleResponse.IsSuccessStatusCode, $"Failed.  Actual response {createCircleResponse.StatusCode}");

                var getCircleDefinitionsResponse = await svc.GetCircleDefinitions();
                Assert.IsTrue(getCircleDefinitionsResponse.IsSuccessStatusCode, $"Failed.  Actual response {getCircleDefinitionsResponse.StatusCode}");

                var definitionList = getCircleDefinitionsResponse.Content;
                Assert.IsNotNull(definitionList);

                var deleteCircleResponse = await svc.DeleteCircle(definitionList.Single().Id);
                Assert.IsTrue(deleteCircleResponse.IsSuccessStatusCode, $"Failed.  Actual response {deleteCircleResponse.StatusCode}");

                var secondGetCircleDefinitionsResponse = await svc.GetCircleDefinitions();
                Assert.IsTrue(secondGetCircleDefinitionsResponse.IsSuccessStatusCode, $"Failed.  Actual response {secondGetCircleDefinitionsResponse.StatusCode}");
                var emptyDefinitionList = secondGetCircleDefinitionsResponse.Content;
                Assert.IsNotNull(emptyDefinitionList);
                Assert.IsTrue(!emptyDefinitionList.Any());
            }
        }
    }
}