using System.IO;
using NUnit.Framework;
using Odin.Core.Util;

namespace Odin.Core.Tests.Util;

public class TempDirectoryTest
{
    [Test]
    public void ItShouldCreateDirectoryInSystemOrUserTempFolder()
    {
        var dir = TempDirectory.Create();
        Assert.IsTrue(Directory.Exists(dir));
        Directory.Delete(dir);
    }
}