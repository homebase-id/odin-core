using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Refit;
using Youverse.Core;

namespace Youverse.Hosting.Tests;

public static class RefitCreator
{
    public static T RestServiceFor<T>(HttpClient client, byte[] sharedSecret)
    {
        return RestServiceFor<T>(client, sharedSecret.ToSensitiveByteArray());
    }

    /// <summary>
    /// Creates a Refit service using the shared secret encrypt/decrypt wrapper
    /// </summary>
    public static T RestServiceFor<T>(HttpClient client, SensitiveByteArray sharedSecret)
    {
        var settings = new RefitSettings(new SharedSecretSystemTextJsonContentSerializer(sharedSecret)/*, new SharedSecretUrlParameterFormatter(sharedSecret)*/);
        return RestService.For<T>(client, settings);
    }
}