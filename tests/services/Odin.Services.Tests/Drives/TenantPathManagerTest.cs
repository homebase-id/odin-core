using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Odin.Core.Time;
using Odin.Services.Base;
using Odin.Services.Configuration;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Email;
using System.IO;
using System;
using Odin.Core.Identity;
using NUnit.Framework.Legacy;

namespace Odin.Services.Tests.Config;

public class OdinPathManagerTest
{

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

        _context = new TenantContext(
            dotYouRegistryId: Guid.Parse("11111111-1111-1111-1111-111111111111"),
            hostOdinId: new OdinId("frodo.baggins.demo.rocks"),
            sslRoot: "sslRoot",
            storageConfig: storageConfig,
            firstRunToken: null,
            isPreconfigured: true,
            markedForDeletionDate: null);

        _manager = new TenantPathManager(_context);

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
        ClassicAssert.AreEqual(expected, _manager.GetPayloadFilePath(driveId, fileId, payloadKey, payloadUid));
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
        ClassicAssert.AreEqual(expected, _manager.GetThumbnailFilePath(driveId, fileId, payloadKey, payloadUid, 100, 200));
    }
}
