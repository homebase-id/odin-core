#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using Odin.Core.Http;
using Odin.Services.Configuration;
using Odin.Services.Email;
using Odin.Services.Email.Relay;

namespace Odin.Services.Tests.Email.Relay;

/// <summary>
/// SMTP2GO behind a scripted HTTP handler: <paramref name="responder"/> maps a request path to
/// what SMTP2GO answers, and every request is kept in <see cref="Requests"/>.
/// </summary>
internal class Smtp2GoTestRig(
    Func<string, (HttpStatusCode status, string body)> responder,
    NameAndEmailAddress? systemFrom = null)
{
    public const string ApiKey = "api-secret-123";

    public List<(string Path, string? ApiKeyHeader, string Body)> Requests { get; } = [];

    public OdinConfiguration Configuration { get; } = new()
    {
        Email = new OdinConfiguration.EmailSection
        {
            Relay = new OdinConfiguration.RelaySection
            {
                Provider = OdinConfiguration.RelayProvider.Smtp2Go,
                ApiKey = ApiKey,
                ApiBaseUrl = "https://relay.test/v3",
                SystemFrom = systemFrom ?? new NameAndEmailAddress(),
            },
        },
    };

    public Smtp2GoRelayProvider Provider() =>
        new(new Mock<ILogger<Smtp2GoRelayProvider>>().Object, Api());

    public Smtp2GoEmailSender Sender() => new(Configuration, Api(), Provider());

    /// <summary>A /domain/view (or /domain/add) answer holding one domain, in the shape SMTP2GO returned live.</summary>
    public static string DomainsBody(string domain, bool dkimVerified = false, bool rpathVerified = false) => $$$"""
        {"request_id":"x","data":{"domains":[{"domain":{
          "fulldomain":"{{{domain}}}",
          "dkim_expected":"dkim.smtp2go.net","dkim_selector":"s934313","dkim_verified":{{{Json(dkimVerified)}}},"dkim_status":"","dkim_value":"",
          "rpath_expected":"return.smtp2go.net","rpath_selector":"em934313","rpath_verified":{{{Json(rpathVerified)}}},"rpath_status":"","rpath_value":""},
          "trackers":[]}]}}
        """;

    private static string Json(bool value) => value ? "true" : "false";

    private Smtp2GoApi Api()
    {
        var httpClientFactory = new Mock<IDynamicHttpClientFactory>();
        httpClientFactory
            .Setup(x => x.CreateClient(It.IsAny<string>(), It.IsAny<Action<ClientHandlerConfig>?>()))
            .Returns(() => new HttpClient(new RecordingHandler(Requests, responder)));
        return new Smtp2GoApi(Configuration, httpClientFactory.Object);
    }

    private class RecordingHandler(
        List<(string Path, string? ApiKeyHeader, string Body)> requests,
        Func<string, (HttpStatusCode status, string body)> responder) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            var key = request.Headers.TryGetValues("X-Smtp2go-Api-Key", out var values) ? values.Single() : null;
            requests.Add((path, key, await request.Content!.ReadAsStringAsync(cancellationToken)));

            var (status, body) = responder(path);
            return new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }
    }
}
