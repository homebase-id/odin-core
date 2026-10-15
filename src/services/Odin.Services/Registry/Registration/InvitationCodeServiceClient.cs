using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core.Exceptions;
using Odin.Core.Http;
using Odin.Services.Configuration;

namespace Odin.Services.Registry.Registration;

#nullable enable

public record InvitationCodeInfo(string Code, string Owner, int MaxUses, int UsesLeft);

/// <summary>
/// The invitation-code service on SetupHelper is unreachable or answered with an error.
/// </summary>
public class InvitationCodeServiceException(string message, Exception? inner = null) : OdinSystemException(message, inner);

/// <summary>
/// Calls the invitation-code service on SetupHelper (#1908). It holds the codes and counts their
/// uses for every cluster; this host only asks.
/// </summary>
public class InvitationCodeServiceClient(
    OdinConfiguration configuration,
    IDynamicHttpClientFactory httpClientFactory,
    ILogger<InvitationCodeServiceClient> logger)
{
    private const string ApiKeyHeader = "Odin-Api-Key";
    private const string Root = "api/v1/invitation-codes";

    private record CodeRequest(string Code);
    private record CreateRequest(string Owner, int MaxUses);
    private record ValidateResponse(bool Valid);
    private record UseResponse(bool Used);

    public bool IsEnabled => configuration.Registry.InvitationCodeServiceEnabled;

    /// <summary>
    /// Whether the code exists and has a use left. False, not an error, when the service is unreachable:
    /// signup then refuses the code, which is the safe answer.
    /// </summary>
    public async Task<bool> IsValidAsync(string code)
    {
        try
        {
            var response = await PostAsync<ValidateResponse>(SignupClient(), $"{Root}/validate", new CodeRequest(code));
            return response.Valid;
        }
        catch (InvitationCodeServiceException e)
        {
            logger.LogError(e, "Could not validate an invitation code");
            return false;
        }
    }

    /// <summary>
    /// Takes one use of the code; false if it has none left or does not exist.
    /// </summary>
    public async Task<bool> TryUseAsync(string code)
    {
        var response = await PostAsync<UseResponse>(SignupClient(), $"{Root}/use", new CodeRequest(code));
        return response.Used;
    }

    public async Task UndoUseAsync(string code)
    {
        await SendAsync(SignupClient(), HttpMethod.Post, $"{Root}/undo-use", new CodeRequest(code));
    }

    public async Task<List<InvitationCodeInfo>> ListAsync(string owner)
    {
        var response = await SendAsync(SignupClient(), HttpMethod.Get, $"{Root}/?owner={Uri.EscapeDataString(owner)}");
        return await ReadAsync<List<InvitationCodeInfo>>(response);
    }

    public async Task<InvitationCodeInfo> CreateAsync(string owner, int maxUses)
    {
        return await PostAsync<InvitationCodeInfo>(CreateClient(), $"{Root}/", new CreateRequest(owner, maxUses));
    }

    //

    private HttpClient SignupClient() => Client(configuration.Registry.InvitationCodeSignupApiKey);
    private HttpClient CreateClient() => Client(configuration.Registry.InvitationCodeCreateApiKey);

    private HttpClient Client(string apiKey)
    {
        if (!IsEnabled)
        {
            throw new InvitationCodeServiceException("The invitation-code service is not configured");
        }

        var baseUri = new Uri(configuration.Registry.InvitationCodeServiceUrl + "/");
        var client = httpClientFactory.CreateClient($"{nameof(InvitationCodeServiceClient)}:{baseUri.Host}");
        client.BaseAddress = baseUri;
        client.Timeout = TimeSpan.FromSeconds(10);
        client.DefaultRequestHeaders.Add(ApiKeyHeader, apiKey);
        return client;
    }

    private static async Task<T> PostAsync<T>(HttpClient client, string path, object body)
    {
        var response = await SendAsync(client, HttpMethod.Post, path, body);
        return await ReadAsync<T>(response);
    }

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, HttpMethod method, string path,
        object? body = null)
    {
        HttpResponseMessage response;
        try
        {
            using var request = new HttpRequestMessage(method, path);
            if (body != null)
            {
                request.Content = JsonContent.Create(body);
            }
            response = await client.SendAsync(request);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
        {
            throw new InvitationCodeServiceException($"The invitation-code service is unreachable: {e.Message}", e);
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new InvitationCodeServiceException(
                $"The invitation-code service answered {(int)response.StatusCode} to {method} {path}");
        }

        return response;
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response)
    {
        try
        {
            return await response.Content.ReadFromJsonAsync<T>()
                   ?? throw new InvitationCodeServiceException("The invitation-code service sent an empty answer");
        }
        catch (Exception e) when (e is System.Text.Json.JsonException or NotSupportedException)
        {
            throw new InvitationCodeServiceException("The invitation-code service sent an unreadable answer", e);
        }
    }
}
