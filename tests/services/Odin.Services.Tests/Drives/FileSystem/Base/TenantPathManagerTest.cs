using System;
using System.IO;
using NUnit.Framework;
using NUnit.Framework.Legacy;
using Odin.Core.Identity;
using Odin.Core.Time;
using Odin.Services.Base;
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
    public void GetIdentityDatabasePath_ReturnsCorrectPath()
    {
        var tenantId = Guid.NewGuid();
        var tenantPathManager = new TenantPathManager(_config, tenantId);
        var expected = Path.Combine(_config.Host.TenantDataRootPath, "registrations", tenantId.ToString(), "headers", "identity.db");
        Assert.That(tenantPathManager.GetIdentityDatabasePath(), Is.EqualTo(expected));
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


    private (TenantContext, TenantPathManager) Zetup()
    {
        TenantContext _context;
        TenantPathManager _manager;


        var dotYouRegistryId = Guid.Parse("11111111-1111-1111-1111-111111111111");

        Environment.SetEnvironmentVariable("Host__TenantDataRootPath", "");
        Environment.SetEnvironmentVariable("Host__SystemDataRootPath", "");

        _manager = new TenantPathManager(_config, dotYouRegistryId);

        _context = new TenantContext(
            dotYouRegistryId,
            hostOdinId: new OdinId("frodo.baggins.demo.rocks"),
            tenantPathManager: _manager,
            firstRunToken: null,
            isPreconfigured: true,
            markedForDeletionDate: null);

        return (_context, _manager);
    }

    [Test]
    public void GetTenantRootBasePath_ReturnsCorrectPath()
    {
        var (_context,  _manager) = Zetup();

        var expected = Path.Combine(_manager.TenantDataRootPath, "11111111-1111-1111-1111-111111111111");
        var r = _manager.GetTenantRootBasePath();
        ClassicAssert.AreEqual(expected, r);
    }

    [Test]
    public void GetPayloadFilePath_ReturnsCorrectPath()
    {
        var (_context, _manager) = Zetup();

        var driveId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var fileId = Guid.Parse("33333333-3333-3333-3333-3333333333ab");
        var payloadKey = "key1";
        var payloadUid = new UnixTimeUtcUnique(1234567890);
        var expected = Path.Combine(
            _manager.GetStorageDriveBasePath(driveId),
            "a", "b",
            "333333333333333333333333333333ab-key1-1234567890.payload"
        );
        ClassicAssert.AreEqual(expected, _manager.GetPayloadDirectoryAndFileName(driveId, fileId, payloadKey, payloadUid));
    }

    [Test]
    public void GetThumbnailFilePath_ReturnsCorrectPath()
    {
        var (_context, _manager) = Zetup();

        var driveId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var fileId = Guid.Parse("33333333-3333-3333-3333-3333333333ab");
        var payloadKey = "thumb1";
        var payloadUid = new UnixTimeUtcUnique(1234567890);
        var expected = Path.Combine(
            _manager.GetStorageDriveBasePath(driveId),
            "a", "b",
            "333333333333333333333333333333ab-thumb1-1234567890-100x200.thumb"
        );
        ClassicAssert.AreEqual(expected, _manager.GetThumbnailDirectoryandFileName(driveId, fileId, payloadKey, payloadUid, 100, 200));
    }
}
