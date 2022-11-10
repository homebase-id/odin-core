using Youverse.Core.Util;

namespace Youverse.Provisioning.Services.Certificate;

/// <summary>
/// Writes the certificate auth information for the AcmeChallenge to disk.
/// </summary>
public static class CertificateAuthFile
{
    public static void Write(string path, CertificateAuth certificateAuth)
    {
        var finalPath = PathUtil.OsIfy(Path.Combine(path, certificateAuth.Token));
        File.WriteAllText(finalPath, certificateAuth.Auth);
    }

    public static CertificateAuth Read(string path, string token)
    {
        string fullPath = PathUtil.OsIfy(Path.Combine(path, token));
        Console.WriteLine($"CertificateAuth->Reading token at path: [{fullPath}]");
        if (File.Exists(fullPath))
        {
            return new CertificateAuth()
            {
                Token = token,
                Auth = File.ReadAllText(fullPath)
            };
        }

        Console.WriteLine($"CertificateAuth->File not found or inaccessible.");
        return null;
    }

    public static void Delete(string path, string token, bool ignoreErrors = true)
    {
        try
        {
            string fullPath = PathUtil.OsIfy(Path.Combine(path, token));
            File.Delete(fullPath);
        }
        catch
        {
            if (!ignoreErrors)
            {
                throw;
            }
        }
    }
}