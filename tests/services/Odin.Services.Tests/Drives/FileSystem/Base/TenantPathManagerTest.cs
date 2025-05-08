using System;
using System.IO;
using NUnit.Framework;
using Odin.Core.Exceptions;
using Odin.Core.Time;
using Odin.Services.Configuration;
using Odin.Services.Drives.FileSystem.Base;

namespace Odin.Services.Tests.Drives.FileSystem.Base;

public class TenantPathManagerTests
{
    private OdinConfiguration _config = null!;

    [SetUp]
    public void Setup()
    {
        var testRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        _config = new OdinConfiguration
        {
            Host = new OdinConfiguration.HostSection
            {
                SystemDataRootPath = Path.Combine(testRoot, "system"),
                TenantDataRootPath = Path.Combine(testRoot, "tenants")
            }
        };
    }

    //

    [Test]
    public void DoesThrowIfTenantDataRootPathIsEmpty()
    {
        var tenantId = Guid.NewGuid();

        {
            var badConfig = new OdinConfiguration();
            var exception = Assert.Throws<NullReferenceException>(() =>
            {
                _ = new TenantPathManager(badConfig, tenantId);
            });
            Assert.That(exception?.Message, Is.EqualTo("Object reference not set to an instance of an object."));
        }

        {
            var badConfig = new OdinConfiguration
            {
                Host = new OdinConfiguration.HostSection()
            };
            var exception = Assert.Throws<ArgumentNullException>(() =>
            {
                _ = new TenantPathManager(badConfig, tenantId);
            });
            Assert.That(exception?.Message, Is.EqualTo("Value cannot be null. (Parameter 'TenantDataRootPath')"));
        }
    }

    //

    [Test]
    public void AllBasePathsShouldBeCorrect()
    {
        var tenantId = Guid.NewGuid();
        var tenantPathManager = new TenantPathManager(_config, tenantId);

        Assert.That(tenantPathManager.RegistrationPath, Is.EqualTo(Path.Combine(
            _config.Host.TenantDataRootPath,
            "registrations",
            tenantId.ToString())));

        Assert.That(tenantPathManager.HeadersPath, Is.EqualTo(Path.Combine(
            _config.Host.TenantDataRootPath,
            "registrations",
            tenantId.ToString(),
            "headers")));

        Assert.That(tenantPathManager.SslPath, Is.EqualTo(Path.Combine(
            _config.Host.TenantDataRootPath,
            "registrations",
            tenantId.ToString(),
            "ssl")));

        Assert.That(tenantPathManager.TempPath, Is.EqualTo(Path.Combine(
            _config.Host.TenantDataRootPath,
            "registrations",
            tenantId.ToString(),
            "temp")));

        Assert.That(tenantPathManager.TempDrivesPath, Is.EqualTo(Path.Combine(
            _config.Host.TenantDataRootPath,
            "registrations",
            tenantId.ToString(),
            "temp",
            "drives")));

        Assert.That(tenantPathManager.PayloadsPath, Is.EqualTo(Path.Combine(
            _config.Host.TenantDataRootPath,
            "payloads",
            tenantId.ToString())));

        Assert.That(tenantPathManager.PayloadsDrivesPath, Is.EqualTo(Path.Combine(
            _config.Host.TenantDataRootPath,
            "payloads",
            tenantId.ToString(),
            "drives")));
    }


    //

    [Test]
    public void GuidToPathSafeString_ReturnsCorrectPath()
    {
        var guid = Guid.Parse("11111111-abcd-ABCD-1111-111111111111");
        const string expected = "11111111abcdabcd1111111111111111";
        Assert.That(TenantPathManager.GuidToPathSafeString(guid), Is.EqualTo(expected));
    }

    //

    [Test]
    public void GetFilename_ReturnsCorrectName()
    {
        var guid = Guid.Parse("11111111-abcd-ABCD-1111-111111111111");
        {
            const string expected = "11111111abcdabcd1111111111111111.txt";
            Assert.That(TenantPathManager.GetFilename(guid, "TXT"), Is.EqualTo(expected));
        }
        {
            const string expected = "11111111abcdabcd1111111111111111";
            Assert.That(TenantPathManager.GetFilename(guid, ""), Is.EqualTo(expected));
        }
    }

    //

    [Test]
    public void GetDriveInboxStoragePath_ReturnsCorrectPath()
    {
        var tenantId = Guid.NewGuid();
        var tenantPathManager = new TenantPathManager(_config, tenantId);
        var driveId = Guid.Parse("11111111-abcd-ABCD-1111-111111111111");

        Assert.That(tenantPathManager.GetDriveInboxStoragePath(driveId),  Is.EqualTo(Path.Combine(
            _config.Host.TenantDataRootPath,
            "registrations",
            tenantId.ToString(),
            "temp",
            "drives",
            "11111111abcdabcd1111111111111111",
            "inbox")));
    }

    //

    [Test]
    public void GetDriveUploadsStoragePath_ReturnsCorrectPath()
    {
        var tenantId = Guid.NewGuid();
        var tenantPathManager = new TenantPathManager(_config, tenantId);
        var driveId = Guid.Parse("11111111-abcd-ABCD-1111-111111111111");

        Assert.That(tenantPathManager.GetDriveUploadStoragePath(driveId),  Is.EqualTo(Path.Combine(
            _config.Host.TenantDataRootPath,
            "registrations",
            tenantId.ToString(),
            "temp",
            "drives",
            "11111111abcdabcd1111111111111111",
            "uploads")));
    }

    //

    [Test]
    public void GetDrivePayloadPath_ReturnsCorrectPath()
    {
        var tenantId = Guid.NewGuid();
        var tenantPathManager = new TenantPathManager(_config, tenantId);
        var driveId = Guid.Parse("11111111-abcd-ABCD-1111-111111111111");

        Assert.That(tenantPathManager.GetDrivePayloadPath(driveId),  Is.EqualTo(Path.Combine(
            _config.Host.TenantDataRootPath,
            "payloads",
            tenantId.ToString(),
            "drives",
            "11111111abcdabcd1111111111111111",
            "files")));
    }

    //

    [Test]
    public void AssertValidPayloadKey_ThrowsOnBadKey()
    {
        Assert.Throws<OdinClientException>(() => TenantPathManager.AssertValidPayloadKey("bad key"));
        Assert.DoesNotThrow(() => TenantPathManager.AssertValidPayloadKey("abcd1234"));
    }

    //

    [Test]
    public void GetPayloadDirectoryFromGuid_ReturnsCorrectPath()
    {
        var fileId = Guid.Parse("11111111-abcd-ABCD-1111-111111111111");
        Assert.That(TenantPathManager.GetPayloadDirectoryFromGuid(fileId), Is.EqualTo(Path.Combine(
            "1111",
            "11",
            "11",
            "ab")));
    }

    //

    [Test]
    public void GetPayloadDirectory_ReturnsCorrectPath()
    {
        var tenantId = Guid.NewGuid();
        var tenantPathManager = new TenantPathManager(_config, tenantId);
        var driveId = Guid.Parse("11111111-abcd-ABCD-1111-111111111111");
        var fileId = Guid.Parse("21111111-abcd-ABCD-1111-111111111112");

        Assert.That(tenantPathManager.GetPayloadDirectory(driveId, fileId),  Is.EqualTo(Path.Combine(
            _config.Host.TenantDataRootPath,
            "payloads",
            tenantId.ToString(),
            "drives",
            "11111111abcdabcd1111111111111111",
            "files",
            "2111",
            "11",
            "11",
            "ab")));
    }

    //

    [Test]
    public void GetIdentityDatabasePath_ReturnsCorrectPath()
    {
        var tenantId = Guid.NewGuid();
        var tenantPathManager = new TenantPathManager(_config, tenantId);
        var expected = Path.Combine(_config.Host.TenantDataRootPath, "registrations", tenantId.ToString(), "headers", "identity.db");
        Assert.That(tenantPathManager.GetIdentityDatabasePath(), Is.EqualTo(expected));
    }

    //

    [Test]
    public void CreateBasePayloadFileName_ReturnsCorrectFileName()
    {
        var uid = UnixTimeUtcUnique.ZeroTime;
        Assert.That(TenantPathManager.CreateBasePayloadFileName("ABC", uid), Is.EqualTo("abc-0"));
    }

    //

    [Test]
    public void CreateBasePayloadFileNameAndExtension_ReturnsCorrectFileName()
    {
        var uid = UnixTimeUtcUnique.ZeroTime;
        Assert.That(
            TenantPathManager.CreateBasePayloadFileNameAndExtension("ABC", uid),
            Is.EqualTo("abc-0.payload"));
    }

    //

    [Test]
    public void GetPayloadFileName_ReturnsCorrectFileName()
    {
        var tenantId = Guid.NewGuid();
        var tenantPathManager = new TenantPathManager(_config, tenantId);
        var fileId = Guid.Parse("11111111-abcd-ABCD-1111-111111111111");
        var uid = UnixTimeUtcUnique.ZeroTime;
        Assert.That(
            tenantPathManager.GetPayloadFileName(fileId, "key", uid),
            Is.EqualTo("11111111abcdabcd1111111111111111-key-0.payload"));
    }

    //

    [Test]
    public void GetPayloadDirectoryAndFileName_ReturnsCorrectFileName()
    {
        var tenantId = Guid.NewGuid();
        var tenantPathManager = new TenantPathManager(_config, tenantId);
        var driveId = Guid.Parse("11111111-abcd-ABCD-1111-111111111111");
        var fileId = Guid.Parse("21111111-abcd-ABCD-1111-111111111112");
        var uid = UnixTimeUtcUnique.ZeroTime;
        Assert.That(
            tenantPathManager.GetPayloadDirectoryAndFileName(driveId, fileId, "key", uid),
            Is.EqualTo(Path.Combine(
                _config.Host.TenantDataRootPath,
                "payloads",
                tenantId.ToString(),
                "drives",
                "11111111abcdabcd1111111111111111",
                "files",
                "2111",
                "11",
                "11",
                "ab",
                "21111111abcdabcd1111111111111112-key-0.payload")));
    }

    //

    [Test]
    public void CreateThumbnailFileNameAndExtension_ReturnsCorrectFileName()
    {
        var uid = UnixTimeUtcUnique.ZeroTime;
        Assert.That(
            TenantPathManager.CreateThumbnailFileNameAndExtension("abc", uid, 100, 200),
            Is.EqualTo("abc-0-100x200.thumb"));
    }

    //

    [Test]
    public void CreateThumbnailFileExtensionStarStar_ReturnsCorrectFileName()
    {
        var uid = UnixTimeUtcUnique.ZeroTime;
        Assert.That(
            TenantPathManager.CreateThumbnailFileExtensionStarStar("abc", uid),
            Is.EqualTo("abc-0-*x*.thumb"));
    }

    //

    [Test]
    public void GetThumbnailDirectory_ReturnsCorrectPath()
    {
        var tenantId = Guid.NewGuid();
        var tenantPathManager = new TenantPathManager(_config, tenantId);
        var driveId = Guid.Parse("11111111-abcd-ABCD-1111-111111111111");
        var fileId = Guid.Parse("21111111-abcd-ABCD-1111-111111111112");

        Assert.That(tenantPathManager.GetThumbnailDirectory(driveId, fileId),  Is.EqualTo(Path.Combine(
            _config.Host.TenantDataRootPath,
            "payloads",
            tenantId.ToString(),
            "drives",
            "11111111abcdabcd1111111111111111",
            "files",
            "2111",
            "11",
            "11",
            "ab")));
    }

    //

    [Test]
    public void GetThumbnailFileName_ReturnsCorrectFileName()
    {
        var fileId = Guid.Parse("21111111-abcd-ABCD-1111-111111111112");
        var uid = UnixTimeUtcUnique.ZeroTime;
        Assert.That(TenantPathManager.GetThumbnailFileName(fileId, "key", uid, 10, 20),
            Is.EqualTo("21111111abcdabcd1111111111111112-key-0-10x20.thumb"));
    }

    //

    [Test]
    public void GetThumbnailDirectoryAndFileName_ReturnsCorrectPath()
    {
        var tenantId = Guid.NewGuid();
        var tenantPathManager = new TenantPathManager(_config, tenantId);
        var driveId = Guid.Parse("11111111-abcd-ABCD-1111-111111111111");
        var fileId = Guid.Parse("21111111-abcd-ABCD-1111-111111111112");
        var uid = UnixTimeUtcUnique.ZeroTime;

        Assert.That(tenantPathManager.GetThumbnailDirectoryAndFileName(driveId, fileId, "key", uid, 10, 20),
            Is.EqualTo(Path.Combine(
                _config.Host.TenantDataRootPath,
                "payloads",
                tenantId.ToString(),
                "drives",
                "11111111abcdabcd1111111111111111",
                "files",
                "2111",
                "11",
                "11",
                "ab",
                "21111111abcdabcd1111111111111112-key-0-10x20.thumb")));
    }

    //

}
