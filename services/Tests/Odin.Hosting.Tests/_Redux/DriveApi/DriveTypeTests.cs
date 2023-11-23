using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Odin.Core.Services.Authorization.ExchangeGrants;
using Odin.Core.Services.Drives;

namespace Odin.Hosting.Tests._Redux.DriveApi
{
    public class DriveTypeTests
    {
        [Test]
        public void PermissionedDriveComparer()
        {
            var driveGrantRequest1 = new DriveGrantRequest()
            {
                PermissionedDrive = new PermissionedDrive()
                {
                    Drive = new TargetDrive()
                    {
                        Alias = GuidId.FromString("drive_grant_one_alias"),
                        Type = GuidId.FromString("drive_grant_one_type")
                    },
                    Permission = DrivePermission.ReadWrite
                }
            };

            var driveGrant1 = new DriveGrant()
            {
                DriveId = Guid.NewGuid(),
                KeyStoreKeyEncryptedStorageKey = null,
                PermissionedDrive = new PermissionedDrive()
                {
                    Drive = new TargetDrive()
                    {
                        Alias = GuidId.FromString("drive_grant_one_alias"),
                        Type = GuidId.FromString("drive_grant_one_type")
                    },
                    Permission = DrivePermission.ReadWrite,
                }
            };

            var driveGrantRequest2 = new DriveGrantRequest()
            {
                PermissionedDrive = new PermissionedDrive()
                {
                    Drive = new TargetDrive()
                    {
                        Alias = GuidId.FromString("drive_grant_2_alias22"),
                        Type = GuidId.FromString("drive_grant_2_type22")
                    },
                    Permission = DrivePermission.ReadWrite
                }
            };


            var driveGrant2 = new DriveGrant()
            {
                DriveId = Guid.NewGuid(),
                KeyStoreKeyEncryptedStorageKey = null,
                PermissionedDrive = new PermissionedDrive()
                {
                    Drive = new TargetDrive()
                    {
                        Alias = GuidId.FromString("drive_grant_2_alias22"),
                        Type = GuidId.FromString("drive_grant_2_type22")
                    },
                    Permission = DrivePermission.ReadWrite
                }
            };

            var driveGrantRequest3 = new DriveGrantRequest()
            {
                PermissionedDrive = new PermissionedDrive()
                {
                    Drive = new TargetDrive()
                    {
                        Alias = GuidId.FromString("drive_grant_3_alias"),
                        Type = GuidId.FromString("drive_grant_3_type")
                    },
                    Permission = DrivePermission.Read
                }
            };

            var driveGrant3 = new DriveGrant()
            {
                DriveId = Guid.NewGuid(),
                KeyStoreKeyEncryptedStorageKey = null,
                PermissionedDrive = new PermissionedDrive()
                {
                    Drive = new TargetDrive()
                    {
                        Alias = GuidId.FromString("drive_grant_3_alias"),
                        Type = GuidId.FromString("drive_grant_3_type")
                    },
                    Permission = DrivePermission.Read
                }
            };


            var newDriveGrantRequest = new DriveGrantRequest()
            {
                PermissionedDrive = new PermissionedDrive()
                {
                    Drive = new TargetDrive()
                    {
                        Alias = GuidId.FromString("a_totally_new_alias99"),
                        Type = GuidId.FromString("a_totally_new_type99")
                    },
                    Permission = DrivePermission.ReadWrite
                }
            };

            // No differences
            var circleDriveGrantRequests = new List<DriveGrantRequest>() { driveGrantRequest1, driveGrantRequest2, driveGrantRequest3 };
            var existingDriveGrants = new List<DriveGrant>() { driveGrant1, driveGrant2, driveGrant3 };

            var noDiffResult = existingDriveGrants.ExceptBy(
                circleDriveGrantRequests.Select(dgr => dgr.PermissionedDrive).ToList(),
                dg => dg.PermissionedDrive);

            Assert.IsFalse(noDiffResult.Any());

            //
            // New drive for circle
            //
            var circleDriveGrantRequests_withNewDrive = new List<DriveGrantRequest>() { driveGrantRequest1, driveGrantRequest2, driveGrantRequest3, newDriveGrantRequest };
            var existingDriveGrants_missingNewDrive = new List<DriveGrant>() { driveGrant1, driveGrant2, driveGrant3 };
            var newDriveResult = circleDriveGrantRequests_withNewDrive.ExceptBy(
                existingDriveGrants_missingNewDrive.Select(dgr => dgr.PermissionedDrive).ToList(),
                dg => dg.PermissionedDrive);

            Assert.IsTrue(newDriveResult.Single().PermissionedDrive == newDriveGrantRequest.PermissionedDrive);

            //
            // Drive removed from circle
            //
            var circleDriveGrantRequests_WithDriveRemoved = new List<DriveGrantRequest>() { driveGrantRequest1, driveGrantRequest2 };
            var existingDriveGrants_withDriveToBeRemoved = new List<DriveGrant>() { driveGrant1, driveGrant2, driveGrant3 };
            var removedDriveResult = existingDriveGrants_withDriveToBeRemoved.ExceptBy(
                circleDriveGrantRequests_WithDriveRemoved.Select(dgr => dgr.PermissionedDrive),
                dg => dg.PermissionedDrive);
            Assert.IsTrue(removedDriveResult.Single().PermissionedDrive == driveGrant3.PermissionedDrive);
        }

        [Test]
        public void CanCompareTargetDrives()
        {
            var target1 = new TargetDrive()
            {
                Alias = GuidId.FromString("test-test-19999"),
                Type = GuidId.FromString("drive-type-19999")
            };
            var target2 = new TargetDrive()
            {
                Alias = GuidId.FromString("test-test-19999"),
                Type = GuidId.FromString("drive-type-19999")
            };

            var target3 = new TargetDrive()
            {
                Alias = GuidId.FromString("abc123-3333"),
                Type = GuidId.FromString("drive-type-19999")
            };

            var target4 = new TargetDrive()
            {
                Alias = GuidId.FromString("test-test-19999"),
                Type = GuidId.FromString("ekle-iiowc-0944")
            };

            Assert.IsTrue(target1 == target2);

            Assert.IsFalse(target1 == target3);
            Assert.IsFalse(target1 == target4);
            Assert.IsFalse(target2 == target3);
            Assert.IsFalse(target2 == target3);
            Assert.IsFalse(target3 == target4);
        }

        [Test]
        public void CanGetDifferencesInListsOfTargetDrives()
        {
            var target1 = new TargetDrive()
            {
                Alias = GuidId.FromString("test-test-19999"),
                Type = GuidId.FromString("drive-type-19999")
            };

            var target2 = new TargetDrive()
            {
                Alias = GuidId.FromString("i-r-target-two"),
                Type = GuidId.FromString("drive-type-19999")
            };

            var target3 = new TargetDrive()
            {
                Alias = GuidId.FromString("abc123-3333"),
                Type = GuidId.FromString("drive-type-19999")
            };

            var target4 = new TargetDrive()
            {
                Alias = GuidId.FromString("test-test-19999"),
                Type = GuidId.FromString("ekle-iiowc-0944")
            };

            var target5 = new TargetDrive()
            {
                Alias = GuidId.FromString("i-am-alias-99"),
                Type = GuidId.FromString("iam-type-19999")
            };

            var list1 = new List<TargetDrive>() { target1, target2, target3, target4, target5 };
            var list2 = new List<TargetDrive>() { target1, target3, target4 };

            var expected = new List<TargetDrive>() { target2, target5 };
            var diffs = list1.Except(list2);
            CollectionAssert.AreEquivalent(expected, diffs);
        }
    }
}