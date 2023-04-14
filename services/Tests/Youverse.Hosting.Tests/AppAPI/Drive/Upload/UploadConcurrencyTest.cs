using System;
using System.Collections.Generic;
using System.Net;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using Refit;
using Youverse.Core.Exceptions;
using Youverse.Core.Serialization;
using Youverse.Core.Services.Authorization.Acl;
using Youverse.Core.Services.Authorization.ExchangeGrants;
using Youverse.Core.Services.Authorization.Permissions;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Drives;
using Youverse.Core.Services.Drives.FileSystem.Base.Upload;
using Youverse.Core.Storage;

namespace Youverse.Hosting.Tests.AppAPI.Drive.Upload
{
    public class UploadConcurrencyTest
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
        public async Task UploadFileLockedExceptionThrownWhenMultipleUploadsForSameFileHappenConcurrently()
        {
            var ownerClient = _scaffold.CreateOwnerApiClient(TestIdentities.Samwise);

            var appDrive = await ownerClient.Drive.CreateDrive(TargetDrive.NewTargetDrive(), "Chat Drive 1", "", false);
            var appId = Guid.NewGuid();

            var appPermissionsGrant = new PermissionSetGrantRequest()
            {
                Drives = new List<DriveGrantRequest>()
                {
                    new()
                    {
                        PermissionedDrive = new PermissionedDrive()
                        {
                            Drive = appDrive.TargetDriveInfo,
                            Permission = DrivePermission.All
                        }
                    }
                },
                PermissionSet = new PermissionSet(PermissionKeys.All)
            };

            var appRegistration = await ownerClient.Apps.RegisterApp(appId, appPermissionsGrant);
            var appApiClient = _scaffold.CreateAppClient(TestIdentities.Samwise, appId);
            const string payload = "";

            var fileMetadata = new UploadFileMetadata()
            {
                ContentType = "application/json",
                AllowDistribution = false,
                PayloadIsEncrypted = false,
                AppData = new()
                {
                    ContentIsComplete = true,
                    JsonContent = "some content",
                    FileType = 101,
                    GroupId = default,
                    // UniqueId = message.Id,
                },
                AccessControlList = AccessControlList.OwnerOnly
            };

            //upload a file
            var uploadResult = await appApiClient.Drive.UploadFile(FileSystemType.Standard, appDrive.TargetDriveInfo, fileMetadata, payload);

            async Task<(UploadInstructionSet instructionSet, ApiResponse<UploadResult>)> OverwriteFile()
            {
                var (instructionSet, result) = await appApiClient.Drive.UploadRaw(FileSystemType.Standard, uploadResult.File.TargetDrive, fileMetadata, "",
                    overwriteFileId: uploadResult.File.FileId);

                return (instructionSet, result);
            }

            //
            var tasks = new List<Task<(UploadInstructionSet instructionSet, ApiResponse<UploadResult>)>>();
            for (int i = 0; i < 15; i++)
            {
                tasks.Add(OverwriteFile());
            }

            try
            {
                await Task.WhenAll(tasks.ToArray());

                bool atLeastOneLockException = false;
                //see if any of the uploads failed by checking for null.
                tasks.ForEach(task =>
                {
                    var (instructionSet, response) = task.Result;
                    if (response.StatusCode == HttpStatusCode.BadRequest)
                    {
                        var details = DotYouSystemSerializer.Deserialize<ProblemDetails>(response.Error.Content);
                        Assert.IsTrue((YouverseClientErrorCode)int.Parse(details.Extensions["errorCode"].ToString()) ==
                                      YouverseClientErrorCode.UploadedFileLocked);
                        atLeastOneLockException = true;
                    }
                });
                Assert.IsTrue(atLeastOneLockException, "There should have been at least one lock exception");
            }
            catch (AggregateException ae)
            {
                foreach (var ex in ae.InnerExceptions)
                {
                    Console.WriteLine(ex.Message);
                }

                Assert.Fail($"{ae.InnerExceptions.Count} exceptions were thrown.");
            }
        }
    }
}