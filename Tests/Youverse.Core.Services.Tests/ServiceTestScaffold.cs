using System;
using System.Collections.Generic;
using System.Data.Common;
using System.IO;
using MediatR;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.Core;
using Youverse.Core.Cryptography;
using Youverse.Core.Identity;
using Youverse.Core.Services.Authorization.Acl;
using Youverse.Core.Services.Authorization.Apps;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Contacts.Circle;
using Youverse.Core.Services.Drive;
using Youverse.Core.Services.Registry;

namespace Youverse.Core.Services.Tests
{
    public class ServiceTestScaffold
    {
        private readonly string _folder;
        private string? _dataStoragePath;
        private string? _tempStoragePath;
        public ISystemStorage? SystemStorage { get; private set; }
        public DotYouContextAccessor? Context { get; private set; }
        public ILoggerFactory LoggerFactory { get; private set; }

        public IMediator Mediator { get; private set; }

        public IDriveAclAuthorizationService DriveAclAuthorizationService { get; private set; }

        public string? DataStoragePath => _dataStoragePath;

        public ServiceTestScaffold(string folder)
        {
            _folder = folder;
        }

        public void CreateContext()
        {
            var tempPath = Path.Combine(Path.GetTempPath(), "yttests");
            _dataStoragePath = Path.Combine(tempPath, _folder, "data");
            _tempStoragePath = Path.Combine(tempPath, _folder, "temp");

            Console.WriteLine($"_dataStoragePath: [{DataStoragePath}]");
            Console.WriteLine($"_tempStoragePath: [{_tempStoragePath}]");

            Directory.CreateDirectory(DataStoragePath);
            Directory.CreateDirectory(_tempStoragePath);

            var tcontext = new TenantContext();
            tcontext.StorageConfig = new TenantStorageConfig(_dataStoragePath, _tempStoragePath);
            Context = Substitute.For<DotYouContextAccessor>(tcontext, null);
            // Context.Caller = new CallerContext(new DotYouIdentity("unit-tests"), true, new SensitiveByteArray(new byte[16]));
        }


        public void CreateLoggerFactory()
        {
            LoggerFactory = Substitute.For<ILoggerFactory>();
        }

        public void CreateMediator()
        {
            Mediator = Substitute.For<IMediator>();
        }

        public void CreateAuthorizationService()
        {
            DriveAclAuthorizationService = new DriveAclAuthorizationService(this.Context);
        }

        public void LogDataPath()
        {
            if (!Directory.Exists(DataStoragePath))
            {
                Console.WriteLine($"Data path does not exist: {DataStoragePath}");
            }

            var files = Directory.EnumerateFiles(DataStoragePath, "", SearchOption.AllDirectories);

            Console.WriteLine($"Directories and files in :{DataStoragePath}\n\n");
            Console.ForegroundColor = ConsoleColor.Blue;
            foreach (var f in files)
            {
                Console.WriteLine(f);
            }

            Console.ResetColor();
        }

        public void Cleanup()
        {
            if (!string.IsNullOrWhiteSpace(DataStoragePath))
            {
                Directory.Delete(DataStoragePath, true);
            }

            if (!string.IsNullOrWhiteSpace(_tempStoragePath))
            {
                Directory.Delete(_tempStoragePath, true);
            }
        }
    }
}