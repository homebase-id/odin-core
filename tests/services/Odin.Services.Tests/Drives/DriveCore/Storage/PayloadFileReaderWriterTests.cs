using System;
using System.IO;
using NUnit.Framework;
using Odin.Services.Drives.DriveCore.Storage;

namespace Odin.Services.Tests.Drives.DriveCore.Storage;

public class PayloadFileReaderWriterTests
{
    private string _testTempRoot = string.Empty;

    [SetUp]
    public void Setup()
    {
        // Create a unique temp folder for testing
        _testTempRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_testTempRoot);
    }

    //

    [TearDown]
    public void TearDown()
    {
        // Clean up test directory after test
        if (Directory.Exists(_testTempRoot))
        {
            Directory.Delete(_testTempRoot, true);
        }
    }

    //

    [Test]
    public void ItShouldDeleteAFile()
    {
        var fileName = Path.Combine(_testTempRoot, "test.txt");
        File.WriteAllText(fileName, "Hello World");

        Assert.That(File.Exists(fileName), Is.True);

        //var readerWriter = new PayloadFileReaderWriter(fileName);
        // readerWriter.DeleteFile(fileName);
        //
        // Assert.That(File.Exists(fileName), Is.False);
    }

    //

}