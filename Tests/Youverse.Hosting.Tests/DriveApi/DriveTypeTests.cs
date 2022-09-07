using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Youverse.Core;
using Youverse.Core.Services.Authorization.ExchangeGrants;
using Youverse.Core.Services.Drive;

namespace Youverse.Hosting.Tests.DriveApi
{
    public class PermissionedDriveComparer : IEqualityComparer<PermissionedDrive>
    {
        public bool Equals(PermissionedDrive x, PermissionedDrive y)
        {
            if (ReferenceEquals(x, y)) return true;
            if (ReferenceEquals(x, null)) return false;
            if (ReferenceEquals(y, null)) return false;
            if (x.GetType() != y.GetType()) return false;
            return x.Drive == y.Drive && x.Permission == y.Permission;
        }

        public int GetHashCode(PermissionedDrive obj)
        {
            return HashCode.Combine(obj.Drive, (int)obj.Permission);
        }
    }

    public class DriveTypeTests
    {
        // [Test]
        // public void PermissionedDriveComparer()
        // {
        //     //Note: both driveGrantRequest and DriveGrant inherit from PermissionedDrive
        //     // so the test here is to ensure I can compare them and find diffs in lists
        //     var driveGrantRequest1 = new DriveGrantRequest()
        //     {
        //         Drive = new TargetDrive()
        //         {
        //             Alias = ByteArrayId.FromString("test-test-19999"),
        //             Type = ByteArrayId.FromString("drive-type-19999")
        //         },
        //         Permission = DrivePermission.ReadWrite
        //     };
        //
        //     var driveGrantRequest2 = new DriveGrantRequest()
        //     {
        //         Drive = new TargetDrive()
        //         {
        //             Alias = ByteArrayId.FromString("i-r-target-two"),
        //             Type = ByteArrayId.FromString("drive-type-19999")
        //         },
        //         Permission = DrivePermission.ReadWrite
        //     };
        //
        //     var driveGrantRequest3 = new DriveGrantRequest()
        //     {
        //         Drive = new TargetDrive()
        //         {
        //             Alias = ByteArrayId.FromString("abc123-3333"),
        //             Type = ByteArrayId.FromString("drive-type-19999")
        //         },
        //         Permission = DrivePermission.Read
        //     };
        //
        //     var driveGrantRequest4 = new DriveGrantRequest()
        //     {
        //         Drive = new TargetDrive()
        //         {
        //             Alias = ByteArrayId.FromString("test-test-19999"),
        //             Type = ByteArrayId.FromString("ekle-iiowc-0944")
        //         },
        //         Permission = DrivePermission.Read
        //     };
        //
        //     var driveGrantRequest5 = new DriveGrantRequest()
        //     {
        //         Drive = new TargetDrive()
        //         {
        //             Alias = ByteArrayId.FromString("i-am-alias-99"),
        //             Type = ByteArrayId.FromString("iam-type-19999")
        //         },
        //         Permission = DrivePermission.ReadWrite
        //     };
        //
        //     var driveGrant1 = new DriveGrant()
        //     {
        //         DriveId = Guid.NewGuid(),
        //         KeyStoreKeyEncryptedStorageKey = null,
        //         Drive = new TargetDrive()
        //         {
        //             Alias = ByteArrayId.FromString("test-test-19999"),
        //             Type = ByteArrayId.FromString("drive-type-19999")
        //         },
        //         Permission = DrivePermission.ReadWrite,
        //     };
        //
        //     var driveGrant2 = new DriveGrant()
        //     {
        //         DriveId = Guid.NewGuid(),
        //         KeyStoreKeyEncryptedStorageKey = null,
        //         Drive = new TargetDrive()
        //         {
        //             Alias = ByteArrayId.FromString("i-r-target-two"),
        //             Type = ByteArrayId.FromString("drive-type-19999")
        //         },
        //         Permission = DrivePermission.ReadWrite
        //     };
        //
        //     var driveGrant3 = new DriveGrant()
        //     {
        //         DriveId = Guid.NewGuid(),
        //         KeyStoreKeyEncryptedStorageKey = null,
        //         Drive = new TargetDrive()
        //         {
        //             Alias = ByteArrayId.FromString("5555-99999"),
        //             Type = ByteArrayId.FromString("drive-type-19999")
        //         },
        //         Permission = DrivePermission.Read
        //     };
        //
        //     var driveGrant4 = new DriveGrant()
        //     {
        //         DriveId = Guid.NewGuid(),
        //         KeyStoreKeyEncryptedStorageKey = null,
        //         Drive = new TargetDrive()
        //         {
        //             Alias = ByteArrayId.FromString("5555-99999"),
        //             Type = ByteArrayId.FromString("durk8883gpp")
        //         },
        //         Permission = DrivePermission.ReadWrite
        //     };
        //
        //     var driveGrantRequests = new List<DriveGrantRequest>()
        //     {
        //         driveGrantRequest1, driveGrantRequest2, driveGrantRequest3, driveGrantRequest4, driveGrantRequest5
        //     };
        //
        //     var driveGrants = new List<DriveGrant>() { driveGrant1, driveGrant2, driveGrant3, driveGrant4 };
        //
        //     var diff = driveGrants.ExceptBy(driveGrantRequests, dg => dg.DriveId, new PermissionedDriveComparer());
        // }

        [Test]
        public void CanCompareTargetDrives()
        {
            var target1 = new TargetDrive()
            {
                Alias = ByteArrayId.FromString("test-test-19999"),
                Type = ByteArrayId.FromString("drive-type-19999")
            };
            var target2 = new TargetDrive()
            {
                Alias = ByteArrayId.FromString("test-test-19999"),
                Type = ByteArrayId.FromString("drive-type-19999")
            };

            var target3 = new TargetDrive()
            {
                Alias = ByteArrayId.FromString("abc123-3333"),
                Type = ByteArrayId.FromString("drive-type-19999")
            };

            var target4 = new TargetDrive()
            {
                Alias = ByteArrayId.FromString("test-test-19999"),
                Type = ByteArrayId.FromString("ekle-iiowc-0944")
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
                Alias = ByteArrayId.FromString("test-test-19999"),
                Type = ByteArrayId.FromString("drive-type-19999")
            };

            var target2 = new TargetDrive()
            {
                Alias = ByteArrayId.FromString("i-r-target-two"),
                Type = ByteArrayId.FromString("drive-type-19999")
            };

            var target3 = new TargetDrive()
            {
                Alias = ByteArrayId.FromString("abc123-3333"),
                Type = ByteArrayId.FromString("drive-type-19999")
            };

            var target4 = new TargetDrive()
            {
                Alias = ByteArrayId.FromString("test-test-19999"),
                Type = ByteArrayId.FromString("ekle-iiowc-0944")
            };

            var target5 = new TargetDrive()
            {
                Alias = ByteArrayId.FromString("i-am-alias-99"),
                Type = ByteArrayId.FromString("iam-type-19999")
            };

            var list1 = new List<TargetDrive>() { target1, target2, target3, target4, target5 };
            var list2 = new List<TargetDrive>() { target1, target3, target4 };

            var expected = new List<TargetDrive>() { target2, target5 };
            var diffs = list1.Except(list2);
            CollectionAssert.AreEquivalent(expected, diffs);
        }
    }
}