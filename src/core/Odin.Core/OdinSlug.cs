#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;
using Odin.Core.Exceptions;

namespace Odin.Core;

/// <summary>
/// An app, drive, drive-type or circle slug: a name that never needs escaping.  Like
/// <see cref="Identity.OdinId"/>, it validates when it is made, so an invalid one can't exist.
/// </summary>
/// <remarks>
/// <para>
/// A slug is a URL path segment <b>and</b> a wire address other identities resolve against
/// (docs/drive-addressing.md, Slug format).  So the characters are those both of these allow:
/// </para>
/// <list type="bullet">
/// <item>RFC 3986 §2.3, unreserved characters, which never need percent-encoding anywhere in a URI;</item>
/// <item>RFC 1123 §2.1, a host name label: letters, digits and hyphens, not starting or ending with a hyphen.</item>
/// </list>
/// <para>
/// <b>Always lower case.</b>  Whatever it is given is lowercased first, so <c>Chat</c> and <c>chat</c> are the
/// same slug everywhere: in a name, a lookup, a route or a JSON body.  Anything else malformed is refused,
/// never changed: a slug is immutable and ends up in other identities' URLs.
/// </para>
/// <para>
/// <c>.</c>, <c>_</c> and <c>~</c> are left out: <c>.</c> and <c>..</c> mean something in a path (RFC 3986
/// §3.3), and <c>_</c> and <c>~</c> are not label characters.  What is left also needs no escaping in HTML
/// text or attributes, or in a JSON string.
/// </para>
/// </remarks>
[JsonConverter(typeof(OdinSlugConverter))]
public readonly struct OdinSlug : IEquatable<OdinSlug>, IEquatable<string>
{
    public const int MaxLength = 14;

    /// <summary>
    /// Segments a slug must never collide with, because a literal sibling at the same route position
    /// would win.  Empty today: rooting the slug tree at <c>/apps</c> leaves neither slug position with
    /// a literal sibling.  It must grow whenever a literal segment is added at either position.
    /// </summary>
    private static readonly HashSet<string> Reserved = [];

    private readonly string? _value;

    /// <summary>Lowercases, then refuses anything but a well-formed slug, with a client error naming the rule.</summary>
    public OdinSlug(string value) : this(value, "slug")
    {
    }

    private OdinSlug(string? value, string name)
    {
        if (!TryLower(value, out var lowered))
        {
            throw new OdinClientException(
                $"{name} '{value}' is not a valid slug: letters, digits and inner hyphens only, " +
                $"1-{MaxLength} characters.", OdinClientErrorCode.ArgumentError);
        }

        _value = lowered;
    }

    /// <summary>
    /// False only for <c>default(OdinSlug)</c>, which no constructor makes: the one way a slug is absent
    /// (a field left out, a JSON <c>null</c>, a query value not given).
    /// </summary>
    public bool HasValue => _value != null;

    public string Value => _value ?? throw new InvalidOperationException("An empty OdinSlug");

    /// <summary>The same, with <paramref name="name"/> (the field) in the error.</summary>
    public static OdinSlug Create(string? value, string name) => new(value, name);

    /// <summary>
    /// <paramref name="slug"/>, or a client error when a request left it out: the server never names
    /// anything on the caller's behalf (#1901).
    /// </summary>
    public static OdinSlug Required(OdinSlug slug, string name) =>
        slug.HasValue ? slug : throw new OdinClientException($"{name} is required", OdinClientErrorCode.ArgumentError);

    /// <summary>Whether <paramref name="value"/>, lowercased, is a slug.</summary>
    public static bool IsValid([NotNullWhen(true)] string? value) => TryLower(value, out _);

    /// <summary>The slug <paramref name="value"/> names, or false where it names none.</summary>
    public static bool TryParse([NotNullWhen(true)] string? value, out OdinSlug result)
    {
        result = IsValid(value) ? new OdinSlug(value) : default;
        return result.HasValue;
    }

    /// <summary>
    /// <c>^[a-z0-9]([a-z0-9-]*[a-z0-9])?$</c> after lowercasing, checked a character at a time on the value
    /// as given: a regex <c>$</c> also matches before a trailing newline, and lowercasing first would turn
    /// some non-ASCII letters (the Kelvin sign) into ASCII ones.
    /// </summary>
    private static bool TryLower([NotNullWhen(true)] string? value, [NotNullWhen(true)] out string? lowered)
    {
        lowered = null;
        if (string.IsNullOrEmpty(value) || value.Length > MaxLength || value[0] == '-' || value[^1] == '-')
        {
            return false;
        }

        foreach (var c in value)
        {
            if (!char.IsAsciiLetterOrDigit(c) && c != '-')
            {
                return false;
            }
        }

        lowered = value.ToLowerInvariant();
        return !Reserved.Contains(lowered);
    }

    public static implicit operator string(OdinSlug slug) => slug.Value;

    public static explicit operator OdinSlug(string value) => new(value);

    public bool Equals(OdinSlug other) => string.Equals(_value, other._value, StringComparison.Ordinal);

    /// <summary>Lowercases <paramref name="other"/> first, as making a slug does.</summary>
    public bool Equals(string? other)
    {
        if (_value == null || other == null || other.Length != _value.Length)
        {
            return false;
        }

        for (var i = 0; i < other.Length; i++)
        {
            var c = other[i];
            if ((char.IsAsciiLetterUpper(c) ? (char)(c | 0x20) : c) != _value[i])
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// A slug only: a string is not an <see cref="OdinSlug"/>, and its hash code differs, so a mixed
    /// hash set or dictionary would disagree with itself.  Compare with a string through
    /// <see cref="Equals(string)"/> or <c>==</c>.
    /// </summary>
    public override bool Equals(object? obj) => obj is OdinSlug other && Equals(other);

    public override int GetHashCode() => _value?.GetHashCode(StringComparison.Ordinal) ?? 0;

    public static bool operator ==(OdinSlug a, OdinSlug b) => a.Equals(b);

    public static bool operator !=(OdinSlug a, OdinSlug b) => !a.Equals(b);

    // Without these, the implicit conversion to string would make `slug == "Chat"` an ordinal string compare.
    public static bool operator ==(OdinSlug a, string? b) => a.Equals(b);

    public static bool operator !=(OdinSlug a, string? b) => !a.Equals(b);

    public static bool operator ==(string? a, OdinSlug b) => b.Equals(a);

    public static bool operator !=(string? a, OdinSlug b) => !b.Equals(a);

    public override string ToString() => _value ?? string.Empty;
}

/// <summary>
/// A plain JSON string; a malformed one fails the request.  <c>null</c> reads as an empty slug (a field
/// left out), which <see cref="OdinSlug.Required"/> refuses where one is needed, and an empty slug
/// writes as <c>null</c>.
/// </summary>
public sealed class OdinSlugConverter : JsonConverter<OdinSlug>
{
    public override bool HandleNull => true;

    public override OdinSlug Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType == JsonTokenType.Null ? default : new OdinSlug(reader.GetString()!);

    public override void Write(Utf8JsonWriter writer, OdinSlug value, JsonSerializerOptions options)
    {
        if (value.HasValue)
        {
            writer.WriteStringValue(value.Value);
        }
        else
        {
            writer.WriteNullValue();
        }
    }
}
