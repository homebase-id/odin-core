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
    /// <summary>
    /// A JWK with a coordinate outside the field or a point off the curve is invalid input, like malformed base64,
    /// on every import path (#1812; see <c>EccPublicKeyData.FromJwkPublicKey</c>).
    /// </summary>
    [TestFixture]
    public class TestInvalidJwk
    {
        // Key generation and the reply's PBKDF2 are slow, so they run once; each test starts from the original key.
        private EccFullKeyListData _hostKeys = null!;
        private PasswordReply _reply = null!;
        private string _clientJwk = null!;
        private EccFullKeyData _clientEcc = null!;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            _hostKeys = EccKeyListManagement.CreateEccKeyList(EccKeyListManagement.zeroSensitiveKey, 2,
                EccKeyListManagement.DefaultHoursOfflineKey);
            var nonce = NonceData.NewRandomNonce(EccKeyListManagement.GetCurrentKey(_hostKeys));
            _clientEcc = new EccFullKeyData(EccKeyListManagement.zeroSensitiveKey, EccKeySize.P384, 1);
            _reply = PasswordDataManager.CalculatePasswordReply("EnSøienØ", nonce, _clientEcc);
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
            _reply.PublicKeyJwk = OutsideTheField(_clientJwk);

            Assert.Throws<OdinClientException>(() => PasswordDataManager.ParsePasswordEccReply(_reply, _hostKeys));
        }

        [Test]
        public void APointOffTheCurveIsTheClientsError()
        {
            _reply.PublicKeyJwk = OffTheCurve(_clientJwk);

            Assert.Throws<OdinClientException>(() => PasswordDataManager.ParsePasswordEccReply(_reply, _hostKeys));
        }

        // YouAuth and app registration take the client's key in this base64url form.
        [Test]
        public void ABase64UrlPublicKeyOffTheCurveIsInvalidInput()
        {
            var jwk = Base64UrlEncoder.Encode(OffTheCurve(_clientJwk));

            Assert.Throws<OdinClientException>(() => EccPublicKeyData.FromJwkBase64UrlPublicKey(jwk));
        }

        [Test]
        public void APrivateKeyOutsideTheFieldIsInvalidInput()
        {
            var privateJwk = _clientEcc.PrivateKeyJwk(EccKeyListManagement.zeroSensitiveKey);

            Assert.Throws<OdinClientException>(() =>
                EccFullKeyData.FromJwkPrivateKey(EccKeyListManagement.zeroSensitiveKey, OutsideTheField(privateJwk)));
        }

        private static string OutsideTheField(string jwk) =>
            WithMember(jwk, "x", NistNamedCurves.GetByName("P-384").Curve.Field.Characteristic.ToByteArrayUnsigned());

        private static string OffTheCurve(string jwk)
        {
            var y = Base64UrlEncoder.Decode(Members(jwk)["y"]);
            y[^1] ^= 1;
            return WithMember(jwk, "y", y);
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
