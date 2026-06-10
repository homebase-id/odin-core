using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using Odin.Services.Configuration;
using Odin.Services.Drives.DriveCore.Storage;

namespace Odin.Services.Tests.Drives.DriveCore.Storage;

public class DiskFileStoreTests : PayloadReaderWriterBaseTestFixture
{
    private FileReaderWriter _fileReaderWriter = null!;

    [SetUp]
    public void Setup()
    {
        BaseSetup();
        var config = new OdinConfiguration
        {
            Host = new OdinConfiguration.HostSection
            {
                TenantDataRootPath = Path.Combine(TestRootPath, "tenants"),
                FileOperationRetryAttempts = 1,
                FileOperationRetryDelayMs = TimeSpan.FromMilliseconds(1),
                FileWriteChunkSizeInBytes = 4096,
            }
        };
        _fileReaderWriter = new FileReaderWriter(config, new Mock<ILogger<FileReaderWriter>>().Object);
    }

    [TearDown]
    public void TearDown() => BaseTearDown();

    [Test]
    public async Task WriteStream_ThenRead_RoundTrips()
    {
        var sut = new DiskFileStore(_fileReaderWriter);
        var path = Path.Combine(TestRootPath, "d", "f.metadata");
        await sut.EnsureDirectoryAsync(Path.GetDirectoryName(path)!);
        using var ms = new MemoryStream("hi"u8.ToArray());
        var n = await sut.WriteStreamAsync(path, ms);
        Assert.That(n, Is.EqualTo(2));
        Assert.That(await sut.ExistsAsync(path), Is.True);
        Assert.That(await sut.ReadAllBytesAsync(path), Is.EqualTo("hi"u8.ToArray()));
        Assert.That(sut.Backend, Is.EqualTo(StorageBackendType.Disk));
    }

    [Test]
    public async Task DeleteSet_RemovesOnlyMatchingFileId()
    {
        var sut = new DiskFileStore(_fileReaderWriter);
        var dir = Path.Combine(TestRootPath, "drive");
        Directory.CreateDirectory(dir);
        var keep = Guid.NewGuid();
        var drop = Guid.NewGuid();
        File.WriteAllText(Path.Combine(dir, $"{drop:N}.metadata"), "x");
        File.WriteAllText(Path.Combine(dir, $"{drop:N}.p-1.payload"), "x");
        File.WriteAllText(Path.Combine(dir, $"{keep:N}.p-2.payload"), "x");
        await sut.DeleteSetAsync(dir, drop);
        Assert.That(File.Exists(Path.Combine(dir, $"{drop:N}.metadata")), Is.False);
        Assert.That(File.Exists(Path.Combine(dir, $"{drop:N}.p-1.payload")), Is.False);
        Assert.That(File.Exists(Path.Combine(dir, $"{keep:N}.p-2.payload")), Is.True);
    }

    [Test]
    public async Task IngestFrom_DiskToDisk_CopiesFile()
    {
        var sut = new DiskFileStore(_fileReaderWriter);
        var src = Path.Combine(TestRootPath, "src", "a.payload");
        var dst = Path.Combine(TestRootPath, "dst", "b.payload");
        await sut.EnsureDirectoryAsync(Path.GetDirectoryName(src)!);
        await sut.EnsureDirectoryAsync(Path.GetDirectoryName(dst)!);
        using (var ms = new MemoryStream("payload"u8.ToArray()))
            await sut.WriteStreamAsync(src, ms);
        await sut.IngestFromAsync(sut, src, dst);
        Assert.That(await sut.ReadAllBytesAsync(dst), Is.EqualTo("payload"u8.ToArray()));
    }

    [Test]
    public async Task IngestFrom_S3Source_ThrowsDriveFileStoreException()
    {
        var sut = new DiskFileStore(_fileReaderWriter);
        var src = Path.Combine(TestRootPath, "src", "a.payload");
        var dst = Path.Combine(TestRootPath, "dst", "b.payload");
        var s3Source = new FakeS3Store();
        var ex = Assert.CatchAsync<DriveFileStoreException>(() => sut.IngestFromAsync(s3Source, src, dst));
        Assert.That(ex, Is.Not.Null);
        Assert.That(ex!.Message, Does.Contain("S3"));
        await Task.CompletedTask;
    }

    [Test]
    public async Task WriteBytes_ThenReadAll_RoundTrips()
    {
        var sut = new DiskFileStore(_fileReaderWriter);
        var path = Path.Combine(TestRootPath, "bytes", "f.bin");
        await sut.EnsureDirectoryAsync(Path.GetDirectoryName(path)!);
        var data = new byte[] { 10, 20, 30, 40 };
        await sut.WriteBytesAsync(path, data);
        Assert.That(await sut.ReadAllBytesAsync(path), Is.EqualTo(data));
    }

    [Test]
    public async Task ReadBytes_ReturnsSlice()
    {
        var sut = new DiskFileStore(_fileReaderWriter);
        var path = Path.Combine(TestRootPath, "slice", "f.bin");
        await sut.EnsureDirectoryAsync(Path.GetDirectoryName(path)!);
        var data = new byte[] { 1, 2, 3, 4, 5 };
        await sut.WriteBytesAsync(path, data);
        var slice = await sut.ReadBytesAsync(path, 1, 3);
        Assert.That(slice, Is.EqualTo(new byte[] { 2, 3, 4 }));
    }

    [Test]
    public async Task Length_ReturnsFileSize()
    {
        var sut = new DiskFileStore(_fileReaderWriter);
        var path = Path.Combine(TestRootPath, "len", "f.bin");
        await sut.EnsureDirectoryAsync(Path.GetDirectoryName(path)!);
        await sut.WriteBytesAsync(path, new byte[] { 1, 2, 3 });
        Assert.That(await sut.LengthAsync(path), Is.EqualTo(3L));
    }

    [Test]
    public async Task Delete_RemovesFile()
    {
        var sut = new DiskFileStore(_fileReaderWriter);
        var path = Path.Combine(TestRootPath, "del", "f.bin");
        await sut.EnsureDirectoryAsync(Path.GetDirectoryName(path)!);
        await sut.WriteBytesAsync(path, new byte[] { 99 });
        Assert.That(await sut.ExistsAsync(path), Is.True);
        await sut.DeleteAsync(path);
        Assert.That(await sut.ExistsAsync(path), Is.False);
    }

    [Test]
    public async Task DeleteSet_NoThrow_WhenDirMissing()
    {
        var sut = new DiskFileStore(_fileReaderWriter);
        var dir = Path.Combine(TestRootPath, "does-not-exist");
        await sut.DeleteSetAsync(dir, Guid.NewGuid()); // must not throw
    }

    [Test]
    public async Task Exists_ReturnsFalse_WhenMissing()
    {
        var sut = new DiskFileStore(_fileReaderWriter);
        var path = Path.Combine(TestRootPath, "nope.bin");
        Assert.That(await sut.ExistsAsync(path), Is.False);
    }

    // Minimal stub whose only purpose is to report S3 as its backend.
    private sealed class FakeS3Store : IDriveFileStore
    {
        public StorageBackendType Backend => StorageBackendType.S3;
        public Task<uint> WriteStreamAsync(string path, Stream stream, CancellationToken ct = default) => throw new NotImplementedException();
        public Task WriteBytesAsync(string path, byte[] bytes, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<byte[]> ReadAllBytesAsync(string path, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<byte[]> ReadBytesAsync(string path, long start, long length, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<bool> ExistsAsync(string path, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<long> LengthAsync(string path, CancellationToken ct = default) => throw new NotImplementedException();
        public Task DeleteAsync(string path, CancellationToken ct = default) => throw new NotImplementedException();
        public Task DeleteSetAsync(string dir, Guid fileId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task EnsureDirectoryAsync(string dir, CancellationToken ct = default) => throw new NotImplementedException();
        public Task IngestFromAsync(IDriveFileStore source, string sourcePath, string destPath, CancellationToken ct = default) => throw new NotImplementedException();
        public (string bucket, string fullKey)? GetS3Location(string relativePath) => throw new NotImplementedException();
    }
}
