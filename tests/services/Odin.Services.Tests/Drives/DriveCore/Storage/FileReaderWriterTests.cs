using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using Odin.Services.Configuration;
using Odin.Services.Drives.DriveCore.Storage;

namespace Odin.Services.Tests.Drives.DriveCore.Storage;

public class FileReaderWriterTests
{
    private string _testRootPath = string.Empty;
    private OdinConfiguration _config = null!;

    [SetUp]
    public void Setup()
    {
        _testRootPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_testRootPath);

        _config = new OdinConfiguration
        {
            Host = new OdinConfiguration.HostSection
            {
                FileOperationRetryAttempts = 1,
                FileOperationRetryDelayMs = TimeSpan.FromMilliseconds(1),
            }
        };
    }

    //

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_testRootPath))
        {
            Directory.Delete(_testRootPath, true);
        }
    }

    //

    [Test]
    public async Task GetFileBytesAsync_ShouldReadFile()
    {
        var filePath = Path.Combine(_testRootPath, "file.txt");
        var input = new byte[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9 };
        await File.WriteAllBytesAsync(filePath, input);

        var rw = new FileReaderWriter(_config, new Mock<ILogger<FileReaderWriter>>().Object);

        {
            var bytes = await rw.GetFileBytesAsync(filePath, 0, input.Length);
            Assert.That(bytes, Is.EqualTo(input));
        }

        {
            var bytes = await rw.GetFileBytesAsync(filePath, 0, long.MaxValue);
            Assert.That(bytes, Is.EqualTo(input));
        }

        {
            var bytes = await rw.GetFileBytesAsync(filePath, 1, input.Length - 2);
            Assert.That(bytes, Is.EqualTo(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }));
        }

        {
            var bytes = await rw.GetFileBytesAsync(filePath, 1, 1);
            Assert.That(bytes, Is.EqualTo(new byte[] { 1 }));
        }

    }
}