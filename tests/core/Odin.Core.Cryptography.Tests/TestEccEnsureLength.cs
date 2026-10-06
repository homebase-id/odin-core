using NUnit.Framework;
using Odin.Core.Cryptography.Data;
using Odin.Core.Exceptions;

namespace Odin.Core.Cryptography.Tests
{
    /// <summary><c>EnsureLength</c> pads short key material and rejects over-long (#1812; see its comment).</summary>
    [TestFixture]
    public class TestEccEnsureLength
    {
        private class Probe : EccPublicKeyData
        {
            public static byte[] Call(byte[] bytes, int length) => EnsureLength(bytes, length);
        }

        [Test]
        public void AShortValueIsLeftPaddedWithZeros()
        {
            var result = Probe.Call([0x01, 0x02], 4);

            Assert.That(result, Is.EqualTo(new byte[] { 0x00, 0x00, 0x01, 0x02 }));
        }

        [Test]
        public void AValueOfExactlyTheLengthIsReturnedUnchanged()
        {
            var bytes = new byte[] { 0x01, 0x02, 0x03, 0x04 };

            Assert.That(Probe.Call(bytes, 4), Is.EqualTo(bytes));
        }

        [Test]
        public void AValueLongerThanTheLengthIsRejected()
        {
            Assert.Throws<OdinSystemException>(() => Probe.Call([0x01, 0x02, 0x03, 0x04, 0x05], 4));
        }
    }
}
