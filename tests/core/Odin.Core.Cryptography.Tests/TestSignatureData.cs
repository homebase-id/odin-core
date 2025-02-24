using NUnit.Framework;
using Odin.Core;
using Odin.Core.Cryptography;
using Odin.Core.Cryptography.Data;
using Odin.Core.Cryptography.Signatures;
using Odin.Core.Identity;
using Odin.Core.Time;
using Org.BouncyCastle.Security;
using System;
using NUnit.Framework.Legacy;

[TestFixture]
public class SignatureDataTest
{
    /// <summary>
    /// Here's an example Todd of how to sign a request. In the example the string testData is a JSON string
    /// (probably incorrectly formatted) that contains the two parties to be introduced. Frodo is the signatory.
    /// You end up with a singature data-structure that contains who signed it, what their public key was
    /// (you should still validate it), when it was signed. 
    /// Then it calls "verify" with the exact same JSON string, and if it is the same string, the it passes.
    /// 
    /// You would of course need to use Frodo's ECC-384 signature key (the one that requires he is online is the safest)
    /// rather than the key I generated below.
    /// 
    /// What you should be careful of, is making sure that you don't leave room in your code for someone 
    /// signing anything other than an introduction. That's why I made the helper function below. It ensures
    /// that you can't end up signing a random document.
    /// 
    /// As part of transmitting the introduction request you would need to send the exact JSON string as well
    /// as the signature to both parties.
    /// </summary>
    ///        
    private byte[] ToddsHelper(string partyA, string partyB)
    {
        return System.Text.Encoding.UTF8.GetBytes($"introducing: {{ partyA : {partyA}, partyB: {partyB}}}");
    }


    [Test]
    public void Example_Introduction_Signature()
    {
        // Arrange
        byte[] testData = ToddsHelper("sam.gamgee.me", "merry.something.me");
        OdinId testIdentity = new OdinId("frodo.baggins.me");
        SensitiveByteArray testKeyPwd = new SensitiveByteArray(Guid.NewGuid().ToByteArray());
        EccFullKeyData testEccKey = new EccFullKeyData(testKeyPwd, EccKeySize.P384, 1);

        // Act
        SignatureData signature = SignatureData.NewSignature(testData, testIdentity, testKeyPwd, testEccKey);
        ClassicAssert.GreaterOrEqual(signature.Signature.Length, 16);
        bool isValid = SignatureData.Verify(signature, testData);

        // Assert
        ClassicAssert.IsTrue(isValid);
    }


    [Test]
    public void Sign_And_Verify_Valid_Data_Should_Return_True()
    {
        // Arrange
        byte[] testData = System.Text.Encoding.UTF8.GetBytes("Test data");
        OdinId testIdentity = new OdinId("odin.valhalla.com");
        SensitiveByteArray testKeyPwd = new SensitiveByteArray(Guid.NewGuid().ToByteArray());
        EccFullKeyData testEccKey = new EccFullKeyData(testKeyPwd, EccKeySize.P384, 1);

        // Act
        SignatureData signature = SignatureData.NewSignature(testData, testIdentity, testKeyPwd, testEccKey);
        ClassicAssert.GreaterOrEqual(signature.Signature.Length, 16);
        bool isValid = SignatureData.Verify(signature, testData);

        // Assert
        ClassicAssert.IsTrue(isValid);
    }

    [Test]
    public void Sign_And_Verify_Invalid_Data_Should_Return_False()
    {
        // Arrange
        byte[] testData = System.Text.Encoding.UTF8.GetBytes("Test data");
        OdinId testIdentity = new OdinId("odin.valhalla.com");
        SensitiveByteArray testKeyPwd = new SensitiveByteArray(Guid.NewGuid().ToByteArray());
        EccFullKeyData testEccKey = new EccFullKeyData(testKeyPwd, EccKeySize.P384, 1);

        // Sign data
        SignatureData signature = SignatureData.NewSignature(testData, testIdentity, testKeyPwd, testEccKey);

        // Modify signed data
        byte[] modifiedData = System.Text.Encoding.UTF8.GetBytes("Modified data");
        signature.DataHash = ByteArrayUtil.CalculateSHA256Hash(modifiedData);

        // Act
        bool isValid = SignatureData.Verify(signature, testData);

        // Assert
        ClassicAssert.IsFalse(isValid);
    }
}
