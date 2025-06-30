using System;
using System.Collections.Generic;
using System.IO.Hashing;
using System.Net.Http;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace Odin.Core.Http;

#nullable enable

public sealed class ClientConfig
{
    //
    // HttpMessageHandler-level settings (affect handler sharing/creation)
    // Make sure to set these in OdinHttpClientFactory.ConfigureHandler method
    //
    public bool AllowUntrustedServerCertificate { get; set; } = false;
    public X509Certificate2? ClientCertificate { get; set; }
    public TimeSpan HandlerLifetime { get; set; }

    // Handler "middleware" factories
    public List<Func<HttpMessageHandler, HttpMessageHandler>> CustomHandlerFactories { get; set; } = [];

    //

    public string GetHashedString()
    {
        var serialized = SerializeForHashing();
        var bytes = Encoding.UTF8.GetBytes(serialized);
        var hashBytes = XxHash64.Hash(bytes);
        return Convert.ToBase64String(hashBytes);
    }

    //

    private string SerializeForHashing()
    {
        var sb = new StringBuilder();
        sb.Append(AllowUntrustedServerCertificate);
        sb.Append(ClientCertificate?.Thumbprint ?? "");
        sb.Append(CustomHandlerFactories.Count == 0 ? "" : "not-reliable-for-hashing");
        sb.Append(HandlerLifetime.Ticks);
        return sb.ToString();
    }
}


