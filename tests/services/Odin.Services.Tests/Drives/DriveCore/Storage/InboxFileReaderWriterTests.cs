using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using Odin.Core;
using Odin.Services.Configuration;
using Odin.Services.Drives.DriveCore.Storage;

namespace Odin.Services.Tests.Drives.DriveCore.Storage;

public class InboxFileReaderWriterTests
{
    private string _root = "";
    private IInboxReaderWriter _rw = null!;

    [SetUp]
    public void Setup()
    {
        _root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        var config = new OdinConfiguration
        {
            Host = new OdinConfiguration.HostSection
            {
                FileOperationRetryAttempts = 1,
                FileOperationRetryDelayMs = TimeSpan.FromMilliseconds(1),
                FileWriteChunkSizeInBytes = 4096,
            }
        };
        var frw = new FileReaderWriter(config, NullLogger<FileReaderWriter>.Instance);
        _rw = new InboxFileReaderWriter(frw);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    [Test]
    public async Task WriteReadExists_RoundTrips()
    {
        await _rw.EnsureDirectoryAsync(_root);
        var path = Path.Combine(_root, "abc.metadata");
        var bytes = "hello".ToUtf8ByteArray();
        using var ms = new MemoryStream(bytes);

        var written = await _rw.WriteStreamAsync(path, ms);
        Assert.That(written, Is.EqualTo((uint)bytes.Length));
        Assert.That(await _rw.FileExistsAsync(path), Is.True);
        Assert.That(await _rw.GetFileBytesAsync(path), Is.EqualTo(bytes));
    }

    [Test]
    public async Task DeleteByPrefix_RemovesOnlyMatchingFileId()
    {
        await _rw.EnsureDirectoryAsync(_root);
        var fileId = Guid.NewGuid().ToString("N");
        var other = Guid.NewGuid().ToString("N");

        foreach (var ext in new[] { "metadata", "transferkeyheader", "payload" })
        {
            await File.WriteAllTextAsync(Path.Combine(_root, $"{fileId}.{ext}"), "x");
        }
        await File.WriteAllTextAsync(Path.Combine(_root, $"{other}.metadata"), "keep");

        await _rw.DeleteByPrefixAsync(Path.Combine(_root, fileId + "."));

        Assert.That(Directory.GetFiles(_root).Select(Path.GetFileName),
            Is.EquivalentTo(new[] { $"{other}.metadata" }));
    }
}
