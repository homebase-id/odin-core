using System.Linq;
using NUnit.Framework;
using Odin.Core.Cryptography.Crypto;
using Bitcoin.BIP39;
using Odin.Core;

namespace Odin.Core.Cryptography.Tests.BIP39
{
    public class TestBip39ErrorsDontQuoteThePhrase
    {
        [Test]
        public void AMistypedWordIsNotInTheExceptionMessage()
        {
            // A recovery phrase with one word mistyped: exception messages end up in logs, the phrase must not
            var words = BIP39Util.GenerateBIP39(ByteArrayUtil.GetRndByteArray(16)).Split(' ').ToArray();
            words[3] = "zqxwvutyp";

            var e = Assert.Throws<BIP39Exception>(() => BIP39Util.DecodeBIP39(string.Join(' ', words)));
            Assert.That(e!.Message, Does.Not.Contain("zqxwvutyp"), $"actual: {e.Message}");
        }
    }
}
