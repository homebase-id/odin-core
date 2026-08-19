#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;

namespace Odin.Services.Contacts;

/// <summary>
/// A peer's own published profile photo, fetched from their <c>ProfileDrive</c> by
/// <see cref="ContactEnrichmentService"/> and stored on our contact under
/// <see cref="ContactService.PeerImagePayloadKey"/>.
///
/// <para>
/// Deliberately <b>separate</b> from <see cref="ContactService.ProfileImagePayloadKey"/>: that payload
/// holds the photo the <i>user</i> picked for this contact and enrichment must never touch it. Keeping
/// the two independently addressable is what lets a client show the user's override while still being
/// able to fall back to the peer's real photo when the override is removed.
/// </para>
///
/// <para>
/// Bytes here are <b>plaintext</b> — the peer query returns the payload re-encrypted to our shared
/// secret and the enrichment service decrypts it; the contact write then re-encrypts under the contact
/// file's own AES key. Instances are one of two shapes:
/// <list type="bullet">
/// <item><b>A photo</b> — <see cref="Content"/> non-empty; replaces any stored peer photo.</item>
/// <item><see cref="None"/> — the peer has no photo we can see; removes the stored peer photo.</item>
/// </list>
/// A <c>null</c> <see cref="PeerContactImage"/> is distinct from <see cref="None"/>: it means "we did
/// not determine anything this run" and leaves the stored payload untouched.
/// </para>
/// </summary>
public sealed class PeerContactImage
{
    /// <summary>
    /// The peer published no photo visible to us — drop whatever peer photo we hold. Distinct from a
    /// null instance, which means "unknown; change nothing".
    /// </summary>
    public static readonly PeerContactImage None = new();

    /// <summary>Plaintext image bytes. Empty/null marks this instance as a removal.</summary>
    public byte[]? Content { get; init; }

    /// <summary>MIME type of <see cref="Content"/>, e.g. <c>image/jpeg</c>.</summary>
    public string? ContentType { get; init; }

    /// <summary>Plaintext renditions the peer published alongside the full-size image.</summary>
    public List<PeerContactImageThumbnail> Thumbnails { get; init; } = new();

    /// <summary>True when this instance means "the peer has no photo" rather than carrying one.</summary>
    public bool IsRemoval => Content is not { Length: > 0 };

    /// <summary>
    /// Stable digest over the image and every thumbnail, recorded on the stored payload descriptor
    /// (<see cref="Odin.Services.Drives.DriveCore.Storage.PayloadDescriptor.DescriptorContent"/>) so a
    /// later sync can recognize an unchanged photo and skip the rewrite — no version-tag advance and no
    /// change notification for a no-op. Thumbnails are folded in dimension-order so the digest does not
    /// depend on the order the peer happened to list them.
    /// </summary>
    public string ComputeHash()
    {
        using var sha = SHA256.Create();

        void Fold(byte[]? bytes)
        {
            var length = BitConverter.GetBytes(bytes?.Length ?? 0);
            sha.TransformBlock(length, 0, length.Length, null, 0);
            if (bytes is { Length: > 0 })
            {
                sha.TransformBlock(bytes, 0, bytes.Length, null, 0);
            }
        }

        Fold(Content);
        foreach (var thumbnail in Thumbnails.OrderBy(t => t.PixelWidth).ThenBy(t => t.PixelHeight))
        {
            var dimensions = BitConverter.GetBytes(thumbnail.PixelWidth)
                .Concat(BitConverter.GetBytes(thumbnail.PixelHeight))
                .ToArray();
            sha.TransformBlock(dimensions, 0, dimensions.Length, null, 0);
            Fold(thumbnail.Content);
        }

        sha.TransformFinalBlock([], 0, 0);
        return Convert.ToBase64String(sha.Hash!);
    }
}

/// <summary>
/// One rendition of a <see cref="PeerContactImage"/>. Plaintext, like its parent; the contact write
/// encrypts image and thumbnails together under a single payload IV (the convention
/// <see cref="ContactImageThumbnail"/> and <c>ProfileAttributeService</c> already follow).
/// </summary>
public sealed class PeerContactImageThumbnail
{
    public int PixelWidth { get; init; }
    public int PixelHeight { get; init; }
    public string? ContentType { get; init; }
    public byte[]? Content { get; init; }
}
