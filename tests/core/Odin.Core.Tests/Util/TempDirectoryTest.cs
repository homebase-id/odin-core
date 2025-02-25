using System.IO;
using NUnit.Framework;
using NUnit.Framework.Legacy;
using Odin.Core.Util;

namespace Odin.Core.Tests.Util;

public class TempDirectoryTest
{
    [Test]
    public void ItShouldCreateDirectoryInSystemOrUserTempFolder()
    {
        var dir = TempDirectory.Create();
        ClassicAssert.IsTrue(Directory.Exists(dir));
        Directory.Delete(dir);
    }
}