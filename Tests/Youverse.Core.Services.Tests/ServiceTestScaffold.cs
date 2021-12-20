using System;
using System.IO;
using Microsoft.Extensions.Logging;
using NSubstitute;
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
        public DotYouContext Context { get; private set; }

        public ServiceTestScaffold(string folder)
        {
            _folder = folder;
        }

        public void CreateContext()
        {
            var tempPath = Path.GetTempPath();
            _dataStoragePath = Path.Combine(tempPath, _folder, "data");
            _tempStoragePath = Path.Combine(tempPath, _folder, "temp");

            Console.WriteLine($"_dataStoragePath: [{_dataStoragePath}]");
            Console.WriteLine($"_tempStoragePath: [{_tempStoragePath}]");

            Directory.CreateDirectory(_dataStoragePath);
            Directory.CreateDirectory(_tempStoragePath);

            Context = Substitute.For<DotYouContext>();
            Context.StorageConfig = new TenantStorageConfig(_dataStoragePath, _tempStoragePath);
        }

        public void CreateSystemStorage()
        {
            var logger = Substitute.For<ILogger<LiteDbSystemStorage>>();
            SystemStorage = new LiteDbSystemStorage(logger, Context);
        }

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
    }
}