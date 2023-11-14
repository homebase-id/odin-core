using NUnit.Framework;
using Odin.Core;
using Odin.Core.Cryptography;
using Odin.Core.Cryptography.Data;
using Odin.Core.Cryptography.Signatures;
using Odin.Core.Identity;
using Odin.Core.Time;
using System;

[TestFixture]
public class SignatureDataTest
{
    [Test]
    public void Sign_And_Verify_Valid_Data_Should_Return_True()
    {
        // Arrange
        byte[] testData = System.Text.Encoding.UTF8.GetBytes("Test data");
        OdinId testIdentity = new OdinId("odin.valhalla.com");
        SensitiveByteArray testKeyPwd = new SensitiveByteArray(Guid.NewGuid().ToByteArray());
        EccFullKeyData testEccKey = new EccFullKeyData(testKeyPwd, EccFullKeyData.EccKeySize.P384, 1);

        // Act
        SignatureData signature = SignatureData.NewSignature(testData, testIdentity, testKeyPwd, testEccKey);
        Assert.GreaterOrEqual(signature.Signature.Length, 16);
        bool isValid = SignatureData.Verify(signature, testData);

        // Assert
        Assert.IsTrue(isValid);
    }

    [Test]
    public void Sign_And_Verify_Invalid_Data_Should_Return_False()
    {
        // Arrange
        byte[] testData = System.Text.Encoding.UTF8.GetBytes("Test data");
        OdinId testIdentity = new OdinId("odin.valhalla.com");
        SensitiveByteArray testKeyPwd = new SensitiveByteArray(Guid.NewGuid().ToByteArray());
        EccFullKeyData testEccKey = new EccFullKeyData(testKeyPwd, EccFullKeyData.EccKeySize.P384, 1);

        // Sign data
        SignatureData signature = SignatureData.NewSignature(testData, testIdentity, testKeyPwd, testEccKey);

        // Modify signed data
        byte[] modifiedData = System.Text.Encoding.UTF8.GetBytes("Modified data");
        signature.DataHash = ByteArrayUtil.CalculateSHA256Hash(modifiedData);

        // Act
        bool isValid = SignatureData.Verify(signature, testData);

        // Assert
        Assert.IsFalse(isValid);
    }
}
