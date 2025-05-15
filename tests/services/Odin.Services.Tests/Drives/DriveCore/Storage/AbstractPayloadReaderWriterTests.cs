using System;
using System.IO;
using System.Threading;
using NUnit.Framework;
using Odin.Services.Drives.DriveCore.Storage;

namespace Odin.Services.Tests.Drives.DriveCore.Storage;

public class AbstractPayloadReaderWriterTests
{
    [Test]
    public void ItShouldThrowOnBadFileName()
    {
        Assert.Throws<ArgumentException>(() =>
        {
            AbstractPayloadReaderWriter.ValidateFilename("");
        });

        Assert.Throws<ArgumentException>(() =>
        {
            AbstractPayloadReaderWriter.ValidateFilename(null!);
        });

        Assert.Throws<ArgumentException>(() =>
        {
            AbstractPayloadReaderWriter.ValidateFilename("test.txt*");
        });

        Assert.Throws<ArgumentException>(() =>
        {
            AbstractPayloadReaderWriter.ValidateFilename("/some/path/test.txt");
        });

        Assert.DoesNotThrow(() =>
        {
            AbstractPayloadReaderWriter.ValidateFilename("test.txt");
        });

        Assert.DoesNotThrow(() =>
        {
            AbstractPayloadReaderWriter.ValidateFilename("1aB_-.txt");
        });


    }

}
