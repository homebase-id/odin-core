using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using Youverse.Core.Services.Authentication.YouAuth;
using Youverse.Core.Services.Base;
using NSubstitute;
using Youverse.Core.Cryptography;
using Youverse.Core.Cryptography.Data;
using Youverse.Core.Services.Registry;

#nullable enable
namespace Youverse.Core.Services.Tests.Authentication.YouAuth
{
    [TestFixture]
    public class YouAuthSessionStorageTests
    {
        private string? _dataStoragePath;
        private string? _tempStoragePath;
        private LiteDbSystemStorage? _systemStorage;

        [SetUp]
        public void Setup()
        {
            var tempPath = Path.GetTempPath();
            _dataStoragePath = Path.Combine(tempPath, Guid.NewGuid().ToString());
            _tempStoragePath = Path.Combine(tempPath, Guid.NewGuid().ToString());

            Directory.CreateDirectory(_dataStoragePath);
            Directory.CreateDirectory(_tempStoragePath);

            var logger = Substitute.For<ILogger<LiteDbSystemStorage>>();
            var tenantContext = new TenantContext();
            tenantContext.StorageConfig = new TenantStorageConfig(_dataStoragePath, _tempStoragePath);

            _systemStorage = new LiteDbSystemStorage(logger, tenantContext);
        }

        [TearDown]
        public void Cleanup()
        {
            if (!string.IsNullOrWhiteSpace(_dataStoragePath))
            {
                Directory.Delete(_dataStoragePath, true);
            }

            if (!string.IsNullOrWhiteSpace(_tempStoragePath))
            {
                Directory.Delete(_tempStoragePath, true);
            }
        }

        [Test]
        public void ItShouldLoadWhatItSaved()
        {
            var youAuthSessionStorage = new YouAuthSessionStorage(_systemStorage!);

            var sessionlifetime = TimeSpan.FromDays(1);
            var sessionId = Guid.NewGuid();
            
            var accessRegistrationId = Guid.Empty;

            var session = new YouAuthSession(sessionId, "samtheman", sessionlifetime, accessRegistrationId);

            youAuthSessionStorage.Save(session);
            var copy = youAuthSessionStorage.LoadFromId(session.Id);

            Assert.AreEqual(session.Id, copy!.Id);
            Assert.That(session.CreatedAt, Is.EqualTo(copy!.CreatedAt).Within(TimeSpan.FromMilliseconds(1)));
            Assert.That(session.ExpiresAt, Is.EqualTo(copy!.ExpiresAt).Within(TimeSpan.FromMilliseconds(1)));
            Assert.AreEqual(session.Subject, copy!.Subject);

            Assert.That(session.AccessRegistrationId, Is.EqualTo(copy.AccessRegistrationId));
        }
    }
}