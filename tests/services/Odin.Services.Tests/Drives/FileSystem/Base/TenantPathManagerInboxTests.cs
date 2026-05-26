using System;
using System.IO;
using NUnit.Framework;
using Odin.Services.Configuration;
using Odin.Services.Drives.FileSystem.Base;

namespace Odin.Services.Tests.Drives.FileSystem.Base;

public class TenantPathManagerInboxTests
{
    private static OdinConfiguration Config(bool s3Inbox) => new()
    {
        Host = new OdinConfiguration.HostSection { TenantDataRootPath = "/data/tenants" },
        S3InboxStorage = new OdinConfiguration.S3InboxStorageSection { Enabled = s3Inbox },
    };

    [Test]
    public void Disk_InboxFilePath_IsUnderRegistrations()
    {
        var tenantId = Guid.NewGuid();
        var driveId = Guid.NewGuid();
        var fileId = Guid.NewGuid();
        var tpm = new TenantPathManager(Config(s3Inbox: false), tenantId);

        Assert.That(tpm.S3InboxEnabled, Is.False);
        var path = tpm.GetDriveInboxFilePath(driveId, fileId, "metadata");
        Assert.That(path, Does.Contain(Path.Combine("registrations", tenantId.ToString())));
        Assert.That(path, Does.EndWith($"{fileId:N}.metadata"));
    }

    [Test]
    public void S3_InboxFilePath_IsTenantAnchoredKey()
    {
        var tenantId = Guid.NewGuid();
        var driveId = Guid.NewGuid();
        var fileId = Guid.NewGuid();
        var tpm = new TenantPathManager(Config(s3Inbox: true), tenantId);

        Assert.That(tpm.S3InboxEnabled, Is.True);
        var path = tpm.GetDriveInboxFilePath(driveId, fileId, "metadata").Replace('\\', '/');
        Assert.That(path, Is.EqualTo($"{tenantId:N}/drives/{driveId:N}/{fileId:N}.metadata"));
    }

    [Test]
    public void S3_InboxFilePrefix_HasTrailingDot()
    {
        var tenantId = Guid.NewGuid();
        var driveId = Guid.NewGuid();
        var fileId = Guid.NewGuid();
        var tpm = new TenantPathManager(Config(s3Inbox: true), tenantId);

        var prefix = tpm.GetDriveInboxFilePrefix(driveId, fileId).Replace('\\', '/');
        Assert.That(prefix, Is.EqualTo($"{tenantId:N}/drives/{driveId:N}/{fileId:N}."));
    }
}
