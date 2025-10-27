using System;
using Odin.Core.Cryptography;
using Odin.Core.Cryptography.Data;

public class OwnerConsoleToken : IDisposable
{
    ~OwnerConsoleToken()
    {
        this.Dispose();
    }

    public Guid Id { get; set; }

    /// <summary>
    /// Point in time the token expires
    /// </summary>
    public Int64 ExpiryUnixTime { get; set; }

    /// <summary>
    /// The Server's 1/2 of the KeK
    /// </summary>
    public SymmetricKeyEncryptedXor TokenEncryptedKek { get; set; }

    /// <summary>
    /// The shared secret between the client and the host
    /// </summary>
    public byte[] SharedSecret { get; set; }

    public NonceTable NonceKeeper { get; set; }

    public void Dispose()
    {
        // TODO: How to delete ServerHalfOwnerConsoleKey ?
        // ByteArrayUtil.WipeByteArray(this.HalfKey);
        //ByteArrayUtil.WipeByteArray(this.SharedSecret);
    }
}