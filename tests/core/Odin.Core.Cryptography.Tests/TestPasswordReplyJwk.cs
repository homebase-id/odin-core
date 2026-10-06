using System.Collections.Generic;
using System.Text.Json;
using NUnit.Framework;
using Odin.Core.Cryptography.Crypto;
using Odin.Core.Cryptography.Data;
using Odin.Core.Cryptography.Login;
using Odin.Core.Exceptions;
using Org.BouncyCastle.Asn1.Nist;
using Org.BouncyCastle.Math;
using Org.BouncyCastle.Utilities;

namespace Odin.Core.Cryptography.Tests
{
    /// <summary>
    /// The client's public key in a password reply comes off the wire (#1812). BouncyCastle already rejects a
    /// coordinate outside the field and a point off the curve; the login path reports that as the client's bad
    /// input, like a malformed JWK, rather than letting an <c>ArgumentException</c> escape as a server error.
    /// </summary>
    [TestFixture]
    public class TestPasswordReplyJwk
    {
        private EccFullKeyListData _hostKeys = null!;
        private PasswordReply _reply = null!;

        [SetUp]
        public void Setup()
        {
            _hostKeys = EccKeyListManagement.CreateEccKeyList(EccKeyListManagement.zeroSensitiveKey, 2,
                EccKeyListManagement.DefaultHoursOfflineKey);
            var nonce = NonceData.NewRandomNonce(EccKeyListManagement.GetCurrentKey(_hostKeys));
            var clientEcc = new EccFullKeyData(EccKeyListManagement.zeroSensitiveKey, EccKeySize.P384, 1);
            _reply = PasswordDataManager.CalculatePasswordReply("EnSøienØ", nonce, clientEcc);
        }

        [Test]
        public void AValidReplyParses()
        {
            Assert.DoesNotThrow(() => PasswordDataManager.ParsePasswordEccReply(_reply, _hostKeys));
        }

        [Test]
        public void ACoordinateOutsideTheFieldIsTheClientsError()
        {
            var p = NistNamedCurves.GetByName("P-384").Curve.Field.Characteristic;
            _reply.PublicKeyJwk = WithMember(_reply.PublicKeyJwk, "x", BigIntegers.AsUnsignedByteArray(p));

            Assert.Throws<OdinClientException>(() => PasswordDataManager.ParsePasswordEccReply(_reply, _hostKeys));
        }

        [Test]
        public void APointOffTheCurveIsTheClientsError()
        {
            var y = new BigInteger(1, Base64UrlEncoder.Decode(Members(_reply.PublicKeyJwk)["y"]));
            _reply.PublicKeyJwk = WithMember(_reply.PublicKeyJwk, "y", BigIntegers.AsUnsignedByteArray(48, y.Add(BigInteger.One)));

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
