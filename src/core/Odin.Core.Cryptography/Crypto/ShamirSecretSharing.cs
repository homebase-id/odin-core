using System;
using System.Collections.Generic;
using System.Linq;
using Org.BouncyCastle.Math;
using Org.BouncyCastle.Security;

public class ShamirSecretSharing
{
    // If Homebase grows to a very large network, ideally each server would have its
    // own prime number, we could eventually move this to a config file setting.
    private static readonly BigInteger PRIME = new BigInteger("189329554115601632036302071042466348524091279005500917236488474462559403412027");

    public record ShamirShare(int Index, byte[] Share);

    public static List<ShamirShare> GenerateShamirShares(int totalShards, int minShards, byte[] secret)
    {
        ArgumentNullException.ThrowIfNull(secret);
        if (totalShards < minShards) throw new ArgumentException("Total shards must be >= minimum shards.");
        if (minShards < 2) throw new ArgumentException("Minimum shards must be at least 2.");
        if (secret.Length == 0) throw new ArgumentException("Secret cannot be empty.");

        BigInteger secretInt = new BigInteger(1, secret);
        if (secretInt.CompareTo(PRIME) >= 0) throw new ArgumentException("Secret is too large for the fixed prime.");

        SecureRandom random = new SecureRandom();

        SecretShare[] internalShares = Split(secretInt, minShards, totalShards, PRIME, random);

        return internalShares.Select(s => new ShamirShare(s.Number, s.Share.ToByteArrayUnsigned())).ToList();
    }

    public static byte[] ReconstructShamirSecret(List<ShamirShare> shares)
    {
        ArgumentNullException.ThrowIfNull(shares);
        if (shares.Count < 2) throw new ArgumentException("At least 2 shares are required for reconstruction.");

        // Note: This does not enforce the minimum threshold; it assumes the provided shares meet or exceed it.
        SecretShare[] internalShares = shares.Select(s => new SecretShare(s.Index, new BigInteger(1, s.Share))).ToArray();

        BigInteger reconstructed = Combine(internalShares, PRIME);

        return reconstructed.ToByteArrayUnsigned();
    }

    private class SecretShare
    {
        public int Number { get; }
        public BigInteger Share { get; }

        public SecretShare(int number, BigInteger share)
        {
            Number = number;
            Share = share;
        }
    }

    private static SecretShare[] Split(BigInteger secret, int needed, int available, BigInteger prime, SecureRandom random)
    {
        BigInteger[] coeff = new BigInteger[needed];
        coeff[0] = secret;
        for (int i = 1; i < needed; i++)
        {
            BigInteger r;
            for (; ; )
            {
                r = new BigInteger(prime.BitLength, random);
                if (r.CompareTo(BigInteger.Zero) > 0 && r.CompareTo(prime) < 0)
                {
                    break;
                }
            }
            coeff[i] = r;
        }
        SecretShare[] shares = new SecretShare[available];
        for (int x = 1; x <= available; x++)
        {
            BigInteger accum = secret;
            for (int exp = 1; exp < needed; exp++)
            {
                accum = accum.Add(coeff[exp].Multiply(BigInteger.ValueOf(x).ModPow(BigInteger.ValueOf(exp), prime))).Mod(prime);
            }
            shares[x - 1] = new SecretShare(x, accum);
        }
        return shares;
    }

    private static BigInteger Combine(SecretShare[] shares, BigInteger prime)
    {
        BigInteger accum = BigInteger.Zero;
        for (int formula = 0; formula < shares.Length; formula++)
        {
            BigInteger numerator = BigInteger.One;
            BigInteger denominator = BigInteger.One;
            for (int count = 0; count < shares.Length; count++)
            {
                if (formula == count)
                {
                    continue;
                }
                int startposition = shares[formula].Number;
                int nextposition = shares[count].Number;
                numerator = numerator.Multiply(BigInteger.ValueOf(-nextposition)).Mod(prime);
                denominator = denominator.Multiply(BigInteger.ValueOf(startposition - nextposition)).Mod(prime);
            }
            BigInteger value = shares[formula].Share;
            BigInteger tmp = value.Multiply(numerator).Multiply(ModInverse(denominator, prime));
            accum = prime.Add(accum).Add(tmp).Mod(prime);
        }
        return accum;
    }

    private static BigInteger ModInverse(BigInteger k, BigInteger prime)
    {
        k = k.Mod(prime);
        BigInteger r = (k.CompareTo(BigInteger.Zero) == -1) ? (GcdD(prime, k.Negate()).v3).Negate() : GcdD(prime, k).v3;
        return prime.Add(r).Mod(prime);
    }

    private static (BigInteger v1, BigInteger v2, BigInteger v3) GcdD(BigInteger a, BigInteger b)
    {
        if (b.CompareTo(BigInteger.Zero) == 0)
            return (a, BigInteger.One, BigInteger.Zero);
        else
        {
            BigInteger n = a.Divide(b);
            BigInteger c = a.Mod(b);
            (BigInteger v1, BigInteger v2, BigInteger v3) r = GcdD(b, c);
            return (r.v1, r.v3, r.v2.Subtract(r.v3.Multiply(n)));
        }
    }
}