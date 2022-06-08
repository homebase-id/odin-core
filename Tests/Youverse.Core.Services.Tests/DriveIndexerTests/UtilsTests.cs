using NUnit.Framework;
using Youverse.Core.Services.Drive.Query.Sqlite.Storage;

namespace Youverse.Core.Services.Tests.DriveIndexerTests
{
    
    public class UtilsTests
    {
        [Test]
        public void swapTest()
        {
            int i1 = 1;
            int i2 = 2;

            Utils.swap(ref i1, ref i2);

            if (i1 != 2)
                Assert.Fail();
            if (i2 != 1)
                Assert.Fail();
        }
    }
}