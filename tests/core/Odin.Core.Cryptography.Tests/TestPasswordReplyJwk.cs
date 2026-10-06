using System.Collections.Generic;
using System.Text.Json;
using NUnit.Framework;
using Odin.Core.Cryptography.Crypto;
using Odin.Core.Cryptography.Data;
using Odin.Core.Cryptography.Login;
using Odin.Core.Exceptions;
using Org.BouncyCastle.Asn1.Nist;

namespace Odin.Core.Cryptography.Tests
{
    /// <summary>A bad client key in a password reply is the client's error (#1812; see ParsePasswordEccReply).</summary>
    [TestFixture]
    public class TestPasswordReplyJwk
    {
        // Key generation and the reply's PBKDF2 are slow, so they run once; each test starts from the original key.
        private EccFullKeyListData _hostKeys = null!;
        private PasswordReply _reply = null!;
        private string _clientJwk = null!;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            _hostKeys = EccKeyListManagement.CreateEccKeyList(EccKeyListManagement.zeroSensitiveKey, 2,
                EccKeyListManagement.DefaultHoursOfflineKey);
            var nonce = NonceData.NewRandomNonce(EccKeyListManagement.GetCurrentKey(_hostKeys));
            var clientEcc = new EccFullKeyData(EccKeyListManagement.zeroSensitiveKey, EccKeySize.P384, 1);
            _reply = PasswordDataManager.CalculatePasswordReply("EnSøienØ", nonce, clientEcc);
            _clientJwk = _reply.PublicKeyJwk;
        }

        [SetUp]
        public void Setup() => _reply.PublicKeyJwk = _clientJwk;

        [Test]
        public void AValidReplyParses()
        {
            Assert.DoesNotThrow(() => PasswordDataManager.ParsePasswordEccReply(_reply, _hostKeys));
        }

        [Test]
        public void ACoordinateOutsideTheFieldIsTheClientsError()
        {
            var p = NistNamedCurves.GetByName("P-384").Curve.Field.Characteristic;
            _reply.PublicKeyJwk = WithMember(_reply.PublicKeyJwk, "x", p.ToByteArrayUnsigned());

            Assert.Throws<OdinClientException>(() => PasswordDataManager.ParsePasswordEccReply(_reply, _hostKeys));
        }

        [Test]
        public void APointOffTheCurveIsTheClientsError()
        {
            var y = Base64UrlEncoder.Decode(Members(_reply.PublicKeyJwk)["y"]);
            y[^1] ^= 1;
            _reply.PublicKeyJwk = WithMember(_reply.PublicKeyJwk, "y", y);

            Assert.Throws<OdinClientException>(() => PasswordDataManager.ParsePasswordEccReply(_reply, _hostKeys));
        }

        private static Dictionary<string, string> Members(string jwk) =>
            JsonSerializer.Deserialize<Dictionary<string, string>>(jwk)!;

        private static string WithMember(string jwk, string name, byte[] value)
        {
            var members = Members(jwk);
            members[name] = Base64UrlEncoder.Encode(value);
            return JsonSerializer.Serialize(members);
        }
    }
}
