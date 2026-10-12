using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using Odin.Services.Configuration;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Test.Helpers;

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

    /// <summary>
    /// The byte count was a <c>uint</c>: past 4 GiB it wrapped, failed the "wrote everything" check, and the upload
    /// was rejected (#1893). Explicit because it writes 4 GiB to the temp directory.
    /// </summary>
    [Test, Explicit("Writes 4 GiB + 1 byte to disk")]
    public async Task WriteStreamAsync_ReportsTheTrueLengthPast4GiB()
    {
        _config.Host.FileWriteChunkSizeInBytes = 4 * 1024 * 1024;
        var rw = new FileReaderWriter(_config, new Mock<ILogger<FileReaderWriter>>().Object);
        var filePath = Path.Combine(_testRootPath, "big.bin");
        const Int64 size = 4L * 1024 * 1024 * 1024 + 1;

        var written = await rw.WriteStreamAsync(filePath, new ZeroStream(size));

        Assert.That(written, Is.EqualTo(size));
        Assert.That(new FileInfo(filePath).Length, Is.EqualTo(size));
    }

    /// <summary>A readable, seekable-length stream of <c>length</c> zero bytes that allocates nothing.</summary>
    private sealed class ZeroStream(Int64 length) : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override Int64 Length => length;
        public override Int64 Position { get; set; }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var n = (int)Math.Min(count, length - Position);
            Array.Clear(buffer, offset, n);
            Position += n;
            return n;
        }

        public override void Flush() { }
        public override Int64 Seek(Int64 offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(Int64 value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    [Test]
    public async Task OpenStreamForReading_ReadsTheRequestedWindow()
    {
        var filePath = Path.Combine(_testRootPath, "file.txt");
        var input = new byte[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9 };
        await File.WriteAllBytesAsync(filePath, input);

        var rw = new FileReaderWriter(_config, new Mock<ILogger<FileReaderWriter>>().Object);

        async Task<byte[]> Read(Int64 start, Int64? length)
        {
            await using var stream = rw.OpenStreamForReading(filePath, start, length);
            return await stream.ReadToEndAsync();
        }

        Assert.That(await Read(0, null), Is.EqualTo(input));
        Assert.That(await Read(0, input.Length), Is.EqualTo(input));
        Assert.That(await Read(0, Int64.MaxValue), Is.EqualTo(input), "a range past the end is clamped");
        Assert.That(await Read(1, input.Length - 2), Is.EqualTo(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }));
        Assert.That(await Read(1, 1), Is.EqualTo(new byte[] { 1 }));
        Assert.That(await Read(7, null), Is.EqualTo(new byte[] { 7, 8, 9 }));
        Assert.That(await Read(9, 100), Is.EqualTo(new byte[] { 9 }));
        Assert.That(await Read(10, null), Is.Empty, "start at the end is an empty read");
    }

    [Test]
    public async Task OpenStreamForReading_RejectsAStartPastTheEndAndAnEmptyLength()
    {
        var filePath = Path.Combine(_testRootPath, "file.txt");
        await File.WriteAllBytesAsync(filePath, new byte[10]);

        var rw = new FileReaderWriter(_config, new Mock<ILogger<FileReaderWriter>>().Object);

        Assert.Throws<ArgumentOutOfRangeException>(() => rw.OpenStreamForReading(filePath, 11, null));
        Assert.Throws<ArgumentOutOfRangeException>(() => rw.OpenStreamForReading(filePath, 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => rw.OpenStreamForReading(filePath, -1, null));
    }

    [Test]
    public async Task OpenStreamForReading_StreamsAFileLargerThanItsBuffer()
    {
        var filePath = Path.Combine(_testRootPath, "big.bin");
        var input = new byte[3 * 1024 * 1024 + 17];
        Random.Shared.NextBytes(input);
        await File.WriteAllBytesAsync(filePath, input);

        var rw = new FileReaderWriter(_config, new Mock<ILogger<FileReaderWriter>>().Object);

        const Int64 start = 1024 * 1024 - 3;
        await using var stream = rw.OpenStreamForReading(filePath, start, null);
        Assert.That(stream.Length, Is.EqualTo(input.Length - start));
        Assert.That(await stream.ReadToEndAsync(), Is.EqualTo(input[(int)start..]));
    }

    [Test]
    public async Task OpenStreamForReading_DoesNotBlockADeleteOfTheFile()
    {
        // A download holds the file open while the client reads; an update replacing that version deletes it
        // meanwhile. Linux allows that anyway; on Windows it needs FileShare.Delete
        var filePath = Path.Combine(_testRootPath, "file.txt");
        var input = new byte[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9 };
        await File.WriteAllBytesAsync(filePath, input);

        var rw = new FileReaderWriter(_config, new Mock<ILogger<FileReaderWriter>>().Object);

        await using var stream = rw.OpenStreamForReading(filePath, 0, null);
        rw.DeleteFile(filePath);

        Assert.That(await stream.ReadToEndAsync(), Is.EqualTo(input), "the open reader keeps its bytes");
    }
}
