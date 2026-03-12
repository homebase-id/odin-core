using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using Odin.Core.Time;
using Odin.Services.Configuration;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Storage;

namespace Odin.Services.Tests.Drives.DriveCore.Storage;

[TestFixture]
public class FileHandlerSharedTests
{
    private Mock<FileReaderWriter> _fileReaderWriterMock = null!;
    private Mock<ILogger> _loggerMock = null!;
    private FileHandlerShared _shared = null!;
    private OdinConfiguration _config = null!;

    [SetUp]
    public void Setup()
    {
        _config = new OdinConfiguration { Host = new() { FileOperationRetryAttempts = 1, FileOperationRetryDelayMs = TimeSpan.FromMilliseconds(1) } };
        _fileReaderWriterMock = new Mock<FileReaderWriter>(_config, new Mock<ILogger<FileReaderWriter>>().Object);
        _loggerMock = new Mock<ILogger>();
        _shared = new FileHandlerShared(_fileReaderWriterMock.Object, _loggerMock.Object);
    }

    [Test]
    public void TempFileExists_ReturnsFileReaderWriterResult()
    {
        // Arrange
        const string path = "/test/path";
        _fileReaderWriterMock.Setup(frw => frw.FileExists(path)).Returns(true);

        // Act
        var result = _shared.TempFileExists(path);

        // Assert
        Assert.That(result, Is.True);
        _fileReaderWriterMock.Verify(frw => frw.FileExists(path), Times.Once);
    }

    [Test]
    public async Task GetAllFileBytes_ReturnsBytesAndLogs()
    {
        // Arrange
        const string path = "/test/path";
        var expectedBytes = new byte[] { 1, 2, 3 };
        _fileReaderWriterMock.Setup(frw => frw.GetAllFileBytesAsync(path)).ReturnsAsync(expectedBytes);

        // Act
        var result = await _shared.GetAllFileBytes(path);

        // Assert
        Assert.That(result, Is.EqualTo(expectedBytes));
        _fileReaderWriterMock.Verify(frw => frw.GetAllFileBytesAsync(path), Times.Once);
    }

    [Test]
    public async Task WriteStream_ReturnsBytesWrittenAndLogs()
    {
        // Arrange
        const string path = "/test/path";
        var stream = new MemoryStream(new byte[] { 1, 2, 3 });
        const uint expectedBytesWritten = 3;
        _fileReaderWriterMock.Setup(frw => frw.WriteStreamAsync(path, stream)).ReturnsAsync(expectedBytesWritten);

        // Act
        var result = await _shared.WriteStream(path, stream);

        // Assert
        Assert.That(result, Is.EqualTo(expectedBytesWritten));
        _fileReaderWriterMock.Verify(frw => frw.WriteStreamAsync(path, stream), Times.Once);
    }

    [Test]
    public void GetTempFilenameAndPath_ReturnsCorrectPath()
    {
        // Arrange
        const string dir = "/test/dir";
        var tempFile = new TempFile
        {
            File = new InternalDriveFileId { DriveId = Guid.NewGuid(), FileId = Guid.Parse("12345678-1234-1234-1234-123456789012") }
        };
        const string extension = ".payload";

        // Act
        var result = _shared.GetTempFilenameAndPath(dir, tempFile, extension);

        // Assert
        var expected = Path.Combine(dir, "12345678123412341234123456789012..payload");
        Assert.That(result, Is.EqualTo(expected));
    }
}