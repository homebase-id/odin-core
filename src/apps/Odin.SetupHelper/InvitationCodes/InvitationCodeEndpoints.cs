using System.Security.Cryptography;
using System.Text;
using Odin.Core.Util;

namespace Odin.SetupHelper.InvitationCodes;

/// <summary>
/// The invitation-code API the identity hosts and odin-admin call (#1908). Two keys: the signup key
/// (validate, use, undo-use, list) is held by Homebase-run hosts; the create key by the hosts and
/// odin-admin. Self-hosted servers hold neither.
/// </summary>
public static class InvitationCodeEndpoints
{
    public const string ApiKeyHeader = "Odin-Api-Key";
    public const int MaxUsesLimit = 10_000;

    public record CodeRequest(string? Code);
    public record CreateRequest(string? Owner, int MaxUses);
    public record CodeResponse(string Code, string Owner, int MaxUses, int UsesLeft);

    public static void MapInvitationCodes(this WebApplication app, InvitationCodeStore store, string signupApiKey,
        string createApiKey)
    {
        const string root = "/api/v1/invitation-codes";

        var signup = app.MapGroup(root).AddEndpointFilter(RequireApiKey(signupApiKey));

        signup.MapPost("/validate", async (CodeRequest request) =>
        {
            var code = InvitationCodeFormat.Normalize(request.Code);
            var found = code == null ? null : await store.GetAsync(code);
            return Results.Ok(new { valid = found is { UsesLeft: > 0 } });
        });

        signup.MapPost("/use", async (CodeRequest request) =>
        {
            var code = InvitationCodeFormat.Normalize(request.Code);
            var used = code != null && await store.TryUseAsync(code);
            return Results.Ok(new { used });
        });

        signup.MapPost("/undo-use", async (CodeRequest request) =>
        {
            var code = InvitationCodeFormat.Normalize(request.Code);
            if (code != null)
            {
                await store.UndoUseAsync(code);
            }
            return Results.NoContent();
        });

        signup.MapGet("/", async (string? owner) =>
        {
            var normalizedOwner = NormalizeOwner(owner);
            if (normalizedOwner == null)
            {
                return Results.BadRequest("Invalid owner");
            }
            var codes = await store.ListByOwnerAsync(normalizedOwner);
            return Results.Ok(codes.Select(ToResponse));
        });

        var create = app.MapGroup(root).AddEndpointFilter(RequireApiKey(createApiKey));

        create.MapPost("/", async (CreateRequest request) =>
        {
            var owner = NormalizeOwner(request.Owner);
            if (owner == null)
            {
                return Results.BadRequest("Invalid owner");
            }
            if (request.MaxUses is < 1 or > MaxUsesLimit)
            {
                return Results.BadRequest($"MaxUses must be between 1 and {MaxUsesLimit}");
            }
            var created = await store.CreateAsync(owner, request.MaxUses);
            return Results.Ok(ToResponse(created));
        });
    }

    //

    private static CodeResponse ToResponse(InvitationCode code)
    {
        return new CodeResponse(InvitationCodeFormat.Display(code.Code), code.Owner, code.MaxUses, code.UsesLeft);
    }

    private static string? NormalizeOwner(string? owner)
    {
        var normalized = owner?.Trim().ToLowerInvariant();
        return normalized != null && AsciiDomainNameValidator.TryValidateDomain(normalized) ? normalized : null;
    }

    private static Func<EndpointFilterInvocationContext, EndpointFilterDelegate, ValueTask<object?>> RequireApiKey(
        string apiKey)
    {
        var expected = Encoding.UTF8.GetBytes(apiKey);
        return async (context, next) =>
        {
            var presented = context.HttpContext.Request.Headers[ApiKeyHeader].ToString();
            if (!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(presented), expected))
            {
                return Results.Unauthorized();
            }
            return await next(context);
        };
    }
}
