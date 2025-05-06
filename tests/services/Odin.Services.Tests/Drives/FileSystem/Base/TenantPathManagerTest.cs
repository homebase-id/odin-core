using System;
using System.IO;
using NUnit.Framework;
using Odin.Services.Configuration;
using Odin.Services.Drives.FileSystem.Base;

namespace Odin.Services.Tests.Drives.FileSystem.Base;

public class TenantPathManagerTests
{
    private string _testRoot = "";
    private OdinConfiguration _config = null!;

    [SetUp]
    public void Setup()
    {
        _testRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_testRoot);

        _config = new OdinConfiguration
        {
            Host = new OdinConfiguration.HostSection
            {
                SystemDataRootPath = Path.Combine(_testRoot, "system"),
                TenantDataRootPath = Path.Combine(_testRoot, "tenants")
            }
        };
    }

    //

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_testRoot))
        {
            Directory.Delete(_testRoot, true);
        }
    }

    //

    [Test]
    public void GetIdentityDatabasePath_ReturnsCorrectPath()
    {
        var tenantId = Guid.NewGuid();
        var tenantPathManager = new TenantPathManager(_config, "shard1", tenantId);
        var expected = Path.Combine(_config.Host.TenantDataRootPath, "registrations", tenantId.ToString(), "headers", "identity.db");
        Assert.That(tenantPathManager.GetIdentityDatabasePath(), Is.EqualTo(expected));
    }


/*
    private (TenantContext, TenantPathManager) Setup()
    {
        TenantContext _context;
        TenantPathManager _manager;


        var storageConfig = new TenantStorageConfig(
            headerDataStoragePath: "/header",
            tempStoragePath: "/temp",
            payloadStoragePath: "/payload",
            staticFileStoragePath: "/static",
            payloadShardKey: "shard1");

        var dotYouRegistryId = Guid.Parse("11111111-1111-1111-1111-111111111111");

        Environment.SetEnvironmentVariable("Host__TenantDataRootPath", "");
        Environment.SetEnvironmentVariable("Host__SystemDataRootPath", "");

        _manager = new TenantPathManager(storageConfig.PayloadShardKey, storageConfig.TempStoragePath, storageConfig.PayloadStoragePath, storageConfig.HeaderDataStoragePath, dotYouRegistryId);

        _context = new TenantContext(
            dotYouRegistryId,
            hostOdinId: new OdinId("frodo.baggins.demo.rocks"),
            sslRoot: "sslRoot",
            storageConfig: storageConfig,
            tenantPathManager: _manager,
            firstRunToken: null,
            isPreconfigured: true,
            markedForDeletionDate: null);

        return (_context, _manager);
    }

    [Test]
    public void GetTenantRootBasePath_ReturnsCorrectPath()
    {
        var (_context,  _manager) = Setup();

        var expected = Path.Combine(TenantPathManager.ConfigRoot, "storage", "production", "11111111-1111-1111-1111-111111111111");
        ClassicAssert.AreEqual(expected, _manager.GetTenantRootBasePath());
    }

    [Test]
    public void GetPayloadFilePath_ReturnsCorrectPath()
    {
        var (_context, _manager) = Setup();

        var driveId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var fileId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var payloadKey = "key1";
        var payloadUid = new UnixTimeUtcUnique(1234567890);
        var expected = Path.Combine(
            _manager.GetStorageDriveBasePath(driveId),
            "3333", "33", "33", "33",
            "33333333333333333333333333333333-key1-1234567890.payload"
        );
        ClassicAssert.AreEqual(expected, _manager.GetPayloadDirectoryAndFileName(driveId, fileId, payloadKey, payloadUid));
    }

    [Test]
    public void GetThumbnailFilePath_ReturnsCorrectPath()
    {
        var (_context, _manager) = Setup();

        var driveId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var fileId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var payloadKey = "thumb1";
        var payloadUid = new UnixTimeUtcUnique(1234567890);
        var expected = Path.Combine(
            _manager.GetStorageDriveBasePath(driveId),
            "3333", "33", "33", "33",
            "33333333333333333333333333333333-thumb1-1234567890-100x200.thumb"
        );
        ClassicAssert.AreEqual(expected, _manager.GetThumbnailDirectoryandFileName(driveId, fileId, payloadKey, payloadUid, 100, 200));
    }
*/
}
