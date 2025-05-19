using System;
using System.IO;

namespace Odin.Services.Tests.Drives.DriveCore.Storage;

public abstract class PayloadReaderWriterBaseTestFixture
{
    protected string TestRootPath = string.Empty;

    protected void BaseSetup()
    {
        TestRootPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(TestRootPath);
    }

    //

    protected void BaseTearDown()
    {
        if (Directory.Exists(TestRootPath))
        {
            Directory.Delete(TestRootPath, true);
        }
    }


}