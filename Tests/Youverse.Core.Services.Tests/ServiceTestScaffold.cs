using System;
using System.Data.Common;
using System.IO;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.Core;
using Youverse.Core.Cryptography;
using Youverse.Core.Identity;
using Youverse.Core.Services.Authorization.Apps;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Registry;

namespace Youverse.Core.Services.Tests
{
    public class ServiceTestScaffold
    {
        private readonly string _folder;
        private string? _dataStoragePath;
        private string? _tempStoragePath;
        public ISystemStorage? SystemStorage { get; private set; }
        public DotYouContext? Context { get; private set; }
        public ILoggerFactory LoggerFactory { get; private set; }

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

            Context = Substitute.For<DotYouContext>();
            Context.StorageConfig = new TenantStorageConfig(DataStoragePath, _tempStoragePath);
            Context.Caller = new CallerContext(new DotYouIdentity("unit-tests"), true, new SecureKey(new byte[16]));
        }

        public void CreateSystemStorage()
        {
            var logger = Substitute.For<ILogger<LiteDbSystemStorage>>();
            SystemStorage = new LiteDbSystemStorage(logger, Context);
        }

        public void CreateLoggerFactory()
        {
            LoggerFactory = Substitute.For<ILoggerFactory>();
            ;
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