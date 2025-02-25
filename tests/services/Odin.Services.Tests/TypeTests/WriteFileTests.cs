using System;
using System.IO;
using NUnit.Framework;
using NUnit.Framework.Legacy;

namespace Odin.Services.Tests.TypeTests;

public class WriteFileTests
{
    [Test]
    public void FileLockTest()
    {
        var filepath = Path.GetTempFileName();

        var g1 = Guid.NewGuid();
        var stream1 = new MemoryStream(g1.ToByteArray());
        WriteStream(stream1, filepath);

        var g2 = Guid.NewGuid();
        var stream2 = new MemoryStream(g2.ToByteArray());
        WriteStream(stream2, filepath);

        var bytes = File.ReadAllBytes(filepath);
        ClassicAssert.IsTrue(g2 == new Guid(bytes));
    }

    private uint WriteStream(Stream stream, string filePath)
    {
        var buffer = new byte[1024];

        using (var output = new FileStream(filePath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
        {
            var bytesRead = 0;
            do
            {
                bytesRead = stream.Read(buffer, 0, buffer.Length);
                output.Write(buffer, 0, bytesRead);
            } while (bytesRead > 0);

            var bytesWritten = output.Length;
            // output.Close();
            return (uint)bytesWritten;
        }
    }
}