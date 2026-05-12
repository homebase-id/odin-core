using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using Odin.Core;
using Odin.Core.Cryptography.Crypto;
using Odin.Core.Cryptography.Data;
using Odin.Core.Cryptography.Login;
using Odin.Services.Authentication.Owner;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Hosting.Tests.OwnerApi.Authentication;
using Odin.Hosting.Tests.V2.Hosting;
using Refit;

namespace Odin.Hosting.Tests.V2.Auth;

/// <summary>
/// Drives the owner password-set + authenticate dance against an in-process <see cref="OdinHost"/>.
/// Returns the issued <see cref="ClientAuthenticationToken"/> and shared secret — enough to build
/// any of the V2 client wrappers.
/// </summary>
public static class OwnerLogin
{
    public const string DefaultPassword = "EnSøienØ";

    // SetNewPassword is one-shot per identity; subsequent attempts return 400. We set the password
    // exactly once per (host, identity) pair, then authenticate fresh on every call.
    private static readonly ConcurrentDictionary<(OdinHost, string), bool> _passwordsSet = new();

    public static async Task<(ClientAuthenticationToken token, SensitiveByteArray sharedSecret)> RunAsync(
        OdinHost host,
        string identity,
        string password = DefaultPassword)
    {
        var jar = new CookieContainer();
        var baseUri = new Uri($"https://{identity}/");

        using var handler = new CookieDelegatingHandler(jar, host.Server.CreateHandler());
        using var client = new HttpClient(handler) { BaseAddress = baseUri };
        var svc = RestService.For<IOwnerAuthenticationClient>(client);

        var eccKey = new EccFullKeyData(EccKeyListManagement.zeroSensitiveKey, EccKeySize.P384, 1);

        if (_passwordsSet.TryAdd((host, identity), true))
        {
            var saltsResp = await svc.GenerateNewSalts();
            EnsureSuccess(saltsResp, "GenerateNewSalts");
            var salts = saltsResp.Content!;
            var saltNonce = new NonceData(salts.SaltPassword64, salts.SaltKek64, salts.PublicJwk, salts.CRC)
            {
                Nonce64 = salts.Nonce64
            };
            var setReply = PasswordDataManager.CalculatePasswordReply(password, saltNonce, eccKey);
            EnsureSuccess(await svc.SetNewPassword(setReply), "SetNewPassword");
        }

        var nonceResp = await svc.GenerateAuthenticationNonce();
        EnsureSuccess(nonceResp, "GenerateAuthenticationNonce");
        var clientNonce = nonceResp.Content!;
        var authNonce = new NonceData(clientNonce.SaltPassword64, clientNonce.SaltKek64, clientNonce.PublicJwk, clientNonce.CRC)
        {
            Nonce64 = clientNonce.Nonce64
        };
        var authReply = PasswordDataManager.CalculatePasswordReply(password, authNonce, eccKey);

        var authResp = await svc.Authenticate(authReply);
        EnsureSuccess(authResp, "Authenticate");
        var sharedSecret = authResp.Content!.SharedSecret.ToSensitiveByteArray();

        var cookies = jar.GetCookies(baseUri);
        var tokenCookie = cookies[OwnerAuthConstants.CookieName]?.Value;
        if (string.IsNullOrEmpty(tokenCookie))
        {
            throw new InvalidOperationException($"Authenticate did not set {OwnerAuthConstants.CookieName} cookie");
        }

        var decoded = HttpUtility.UrlDecode(tokenCookie);
        if (!ClientAuthenticationToken.TryParse(decoded, out var token))
        {
            throw new InvalidOperationException($"Could not parse {OwnerAuthConstants.CookieName} cookie: {decoded}");
        }

        return (token, sharedSecret);
    }

    private static void EnsureSuccess<T>(ApiResponse<T> resp, string what)
    {
        if (!resp.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"{what} failed: {(int)resp.StatusCode} {resp.ReasonPhrase}");
        }
    }

    private sealed class CookieDelegatingHandler(CookieContainer jar, HttpMessageHandler inner) : DelegatingHandler(inner)
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var cookieHeader = jar.GetCookieHeader(request.RequestUri!);
            if (!string.IsNullOrEmpty(cookieHeader))
            {
                request.Headers.Remove("Cookie");
                request.Headers.Add("Cookie", cookieHeader);
            }

            var resp = await base.SendAsync(request, ct);

            if (resp.Headers.TryGetValues("Set-Cookie", out var setCookies))
            {
                foreach (var sc in setCookies)
                {
                    jar.SetCookies(request.RequestUri!, sc);
                }
            }

            return resp;
        }
    }
}
