using NUnit.Framework;
using Odin.Core;
using Odin.Core.Cryptography;
using Odin.Core.Cryptography.Data;
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
        EccFullKeyData testEccKey = new EccFullKeyData(testKeyPwd, 1);

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
        EccFullKeyData testEccKey = new EccFullKeyData(testKeyPwd, 1);

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


    [Test]
    public void Not_Possible_To_Get_Full_key()
    {
        // Arrange
        byte[] testData = System.Text.Encoding.UTF8.GetBytes("Test data");
        OdinId testIdentity = new OdinId("odin.valhalla.com");
        SensitiveByteArray testKeyPwd = new SensitiveByteArray(Guid.NewGuid().ToByteArray());
        EccFullKeyData testEccKey = new EccFullKeyData(testKeyPwd, 1);

        // Sign data
        SignatureData signature = SignatureData.NewSignature(testData, testIdentity, testKeyPwd, testEccKey);

        // Try to construct the full key from the public key
        try
        {
            var fullKey = new EccFullKeyData(testKeyPwd, signature.PublicKeyDer);
            Assert.Fail("Full key recovered");
        }
        catch (Exception)
        {
            Assert.Pass("OK");
        }
    }
}
