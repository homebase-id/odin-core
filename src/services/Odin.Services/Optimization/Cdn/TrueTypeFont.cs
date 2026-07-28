#nullable enable

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace Odin.Services.Optimization.Cdn;

/// <summary>
/// A minimal, from-scratch TrueType (.ttf) glyph outline reader: just enough of the spec (table
/// directory, 'head', 'maxp', 'loca', 'glyf', 'cmap' format 4) to turn a character into a set of
/// flattened outline contours in font units. No hinting, no composite glyphs, no kerning -- this
/// exists purely to give <see cref="InitialsAvatarGenerator"/> smooth letterforms without pulling in
/// an image/font library dependency.
/// </summary>
internal sealed class TrueTypeFont
{
    // Quadratic Bezier segments in glyph outlines are flattened into this many straight-line pieces.
    // Letters render at most ~150px tall here, so this is comfortably more than enough to look smooth.
    private const int CurveSegments = 8;

    private readonly byte[] _data;
    private readonly uint _glyfOffset;
    private readonly bool _locaLongFormat;
    private readonly uint[] _locaOffsets;
    private readonly Dictionary<int, ushort> _cmap;
    private readonly Dictionary<ushort, Glyph> _glyphCache = new();

    public ushort UnitsPerEm { get; }

    public int CapHeightUnits { get; }

    internal readonly record struct Glyph(int XMin, int YMin, int XMax, int YMax, List<List<(float X, float Y)>> Contours);

    private static readonly Lazy<TrueTypeFont> LazyDejaVuSans = new(() => LoadEmbedded("DejaVuSans.ttf"));

    public static TrueTypeFont DejaVuSans => LazyDejaVuSans.Value;

    private static TrueTypeFont LoadEmbedded(string fileName)
    {
        var resourceName = $"{typeof(TrueTypeFont).Namespace}.Fonts.{fileName}";
        var assembly = typeof(TrueTypeFont).Assembly;
        using var stream = assembly.GetManifestResourceStream(resourceName)
                            ?? throw new InvalidOperationException($"Embedded font resource '{resourceName}' not found.");

        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return new TrueTypeFont(buffer.ToArray());
    }

    private TrueTypeFont(byte[] data)
    {
        _data = data;

        var numTables = ReadUInt16(4);
        var tables = new Dictionary<string, (uint Offset, uint Length)>();
        for (var i = 0; i < numTables; i++)
        {
            var recordOffset = 12 + i * 16;
            var tag = System.Text.Encoding.ASCII.GetString(_data, recordOffset, 4);
            tables[tag] = (ReadUInt32(recordOffset + 8), ReadUInt32(recordOffset + 12));
        }

        var headOffset = (int)tables["head"].Offset;
        UnitsPerEm = ReadUInt16(headOffset + 18);
        _locaLongFormat = ReadInt16(headOffset + 50) != 0;

        var numGlyphs = ReadUInt16((int)tables["maxp"].Offset + 4);

        var locaOffset = (int)tables["loca"].Offset;
        _locaOffsets = new uint[numGlyphs + 1];
        for (var i = 0; i <= numGlyphs; i++)
        {
            _locaOffsets[i] = _locaLongFormat
                ? ReadUInt32(locaOffset + i * 4)
                : (uint)ReadUInt16(locaOffset + i * 2) * 2;
        }

        _glyfOffset = tables["glyf"].Offset;
        _cmap = ParseCmap((int)tables["cmap"].Offset);

        CapHeightUnits = TryGetGlyph('H', out var h) ? h.YMax - h.YMin : (int)(UnitsPerEm * 0.7);
    }

    /// <summary>
    /// Looks up the glyph outline for a character. Returns false for characters missing from the
    /// font's cmap, or whose glyph is a composite (accented Latin letters are typically composites --
    /// irrelevant here since callers only ever ask for plain A-Z/0-9, already folded to base letters).
    /// </summary>
    public bool TryGetGlyph(char c, out Glyph glyph)
    {
        if (!_cmap.TryGetValue(c, out var glyphIndex))
        {
            glyph = default;
            return false;
        }

        if (_glyphCache.TryGetValue(glyphIndex, out glyph))
        {
            return true;
        }

        if (!TryParseSimpleGlyph(glyphIndex, out glyph))
        {
            return false;
        }

        _glyphCache[glyphIndex] = glyph;
        return true;
    }

    //
    // cmap (format 4 only -- covers the BMP, which is all we need for plain Latin letters/digits)
    //

    private Dictionary<int, ushort> ParseCmap(int cmapOffset)
    {
        var numTables = ReadUInt16(cmapOffset + 2);
        var best = -1;
        var bestScore = -1;
        for (var i = 0; i < numTables; i++)
        {
            var recordOffset = cmapOffset + 4 + i * 8;
            var platformId = ReadUInt16(recordOffset);
            var encodingId = ReadUInt16(recordOffset + 2);
            var subtableOffset = cmapOffset + (int)ReadUInt32(recordOffset + 4);

            if (ReadUInt16(subtableOffset) != 4)
            {
                continue;
            }

            var score = (platformId, encodingId) switch
            {
                (3, 1) => 3, // Windows, Unicode BMP
                (0, _) => 2, // Unicode
                (3, 0) => 1, // Windows, Symbol
                _ => 0
            };

            if (score > bestScore)
            {
                bestScore = score;
                best = subtableOffset;
            }
        }

        if (best < 0)
        {
            throw new InvalidOperationException("No usable (format 4) cmap subtable found in embedded font.");
        }

        return ParseCmapFormat4(best);
    }

    private Dictionary<int, ushort> ParseCmapFormat4(int tableOffset)
    {
        var result = new Dictionary<int, ushort>();

        var segCount = ReadUInt16(tableOffset + 6) / 2;
        var endCodeOffset = tableOffset + 14;
        var startCodeOffset = endCodeOffset + segCount * 2 + 2; // +2 skips reservedPad
        var idDeltaOffset = startCodeOffset + segCount * 2;
        var idRangeOffsetOffset = idDeltaOffset + segCount * 2;

        for (var seg = 0; seg < segCount; seg++)
        {
            var endCode = ReadUInt16(endCodeOffset + seg * 2);
            var startCode = ReadUInt16(startCodeOffset + seg * 2);
            var idDelta = ReadInt16(idDeltaOffset + seg * 2);
            var idRangeOffsetAddress = idRangeOffsetOffset + seg * 2;
            var idRangeOffset = ReadUInt16(idRangeOffsetAddress);

            if (startCode == 0xFFFF && endCode == 0xFFFF)
            {
                continue;
            }

            for (var c = startCode; c <= endCode && c != 0xFFFF; c++)
            {
                ushort glyphIndex;
                if (idRangeOffset == 0)
                {
                    glyphIndex = (ushort)((c + idDelta) & 0xFFFF);
                }
                else
                {
                    var glyphIndexAddress = idRangeOffsetAddress + idRangeOffset + 2 * (c - startCode);
                    var rawIndex = ReadUInt16(glyphIndexAddress);
                    glyphIndex = rawIndex == 0 ? (ushort)0 : (ushort)((rawIndex + idDelta) & 0xFFFF);
                }

                if (glyphIndex != 0)
                {
                    result[c] = glyphIndex;
                }
            }
        }

        return result;
    }

    //
    // glyf (simple glyphs only -- sufficient for plain Latin letters/digits, which are never composites)
    //

    private bool TryParseSimpleGlyph(ushort glyphIndex, out Glyph glyph)
    {
        var start = _glyfOffset + _locaOffsets[glyphIndex];
        var length = _locaOffsets[glyphIndex + 1] - _locaOffsets[glyphIndex];
        if (length == 0)
        {
            glyph = new Glyph(0, 0, 0, 0, []);
            return true;
        }

        var offset = (int)start;
        var numberOfContours = ReadInt16(offset);
        if (numberOfContours < 0)
        {
            glyph = default;
            return false; // composite glyph -- not supported, not expected for plain A-Z/0-9
        }

        var xMin = ReadInt16(offset + 2);
        var yMin = ReadInt16(offset + 4);
        var xMax = ReadInt16(offset + 6);
        var yMax = ReadInt16(offset + 8);

        var p = offset + 10;
        var endPts = new ushort[numberOfContours];
        for (var i = 0; i < numberOfContours; i++)
        {
            endPts[i] = ReadUInt16(p);
            p += 2;
        }

        var numPoints = numberOfContours > 0 ? endPts[numberOfContours - 1] + 1 : 0;

        var instructionLength = ReadUInt16(p);
        p += 2 + instructionLength;

        var flags = new byte[numPoints];
        var fi = 0;
        while (fi < numPoints)
        {
            var flag = _data[p++];
            flags[fi++] = flag;
            if ((flag & 0x08) == 0)
            {
                continue;
            }

            var repeatCount = _data[p++];
            for (var r = 0; r < repeatCount && fi < numPoints; r++)
            {
                flags[fi++] = flag;
            }
        }

        var xs = new int[numPoints];
        var x = 0;
        for (var i = 0; i < numPoints; i++)
        {
            var flag = flags[i];
            if ((flag & 0x02) != 0)
            {
                var dx = _data[p++];
                x += (flag & 0x10) != 0 ? dx : -dx;
            }
            else if ((flag & 0x10) == 0)
            {
                x += ReadInt16(p);
                p += 2;
            }

            xs[i] = x;
        }

        var ys = new int[numPoints];
        var y = 0;
        for (var i = 0; i < numPoints; i++)
        {
            var flag = flags[i];
            if ((flag & 0x04) != 0)
            {
                var dy = _data[p++];
                y += (flag & 0x20) != 0 ? dy : -dy;
            }
            else if ((flag & 0x20) == 0)
            {
                y += ReadInt16(p);
                p += 2;
            }

            ys[i] = y;
        }

        var contours = new List<List<(float X, float Y)>>(numberOfContours);
        var pointStart = 0;
        foreach (var endPt in endPts)
        {
            var count = endPt - pointStart + 1;
            if (count >= 2)
            {
                contours.Add(FlattenContour(xs, ys, flags, pointStart, count));
            }

            pointStart = endPt + 1;
        }

        glyph = new Glyph(xMin, yMin, xMax, yMax, contours);
        return true;
    }

    // Converts a raw on/off-curve TrueType contour into a flattened closed polyline: straight lines
    // between consecutive on-curve points, subdivided quadratic Beziers wherever an off-curve control
    // point appears, with the standard implied-on-curve-midpoint rule for two consecutive off-curve
    // points (and for a contour that starts off-curve).
    private static List<(float X, float Y)> FlattenContour(int[] xs, int[] ys, byte[] flags, int start, int count)
    {
        var raw = new List<(float X, float Y, bool OnCurve)>(count);
        for (var i = 0; i < count; i++)
        {
            var idx = start + i;
            raw.Add((xs[idx], ys[idx], (flags[idx] & 0x01) != 0));
        }

        var seq = new List<(float X, float Y, bool OnCurve)>(count + 1);
        var firstOnCurveIndex = raw.FindIndex(pt => pt.OnCurve);
        if (firstOnCurveIndex < 0)
        {
            // Entirely off-curve contour (valid but rare): synthesize a start point at the midpoint of
            // the first and last points, per the TrueType spec's implied-point rule.
            var mid = Midpoint(raw[0], raw[^1]);
            seq.Add(mid);
            seq.AddRange(raw);
        }
        else
        {
            for (var i = 0; i < count; i++)
            {
                seq.Add(raw[(firstOnCurveIndex + i) % count]);
            }
        }

        seq.Add(seq[0]); // close the loop so the walk below always ends on-curve

        var result = new List<(float X, float Y)> { (seq[0].X, seq[0].Y) };
        var current = seq[0];
        var pos = 1;
        while (pos < seq.Count)
        {
            var pt = seq[pos];
            if (pt.OnCurve)
            {
                result.Add((pt.X, pt.Y));
                current = pt;
                pos++;
                continue;
            }

            (float X, float Y, bool OnCurve) endPoint;
            int advance;
            if (pos + 1 < seq.Count && seq[pos + 1].OnCurve)
            {
                endPoint = seq[pos + 1];
                advance = 2;
            }
            else
            {
                endPoint = Midpoint(pt, seq[pos + 1]);
                advance = 1;
            }

            AppendQuadCurve(result, current, pt, endPoint);
            current = endPoint;
            pos += advance;
        }

        if (result.Count > 1 && result[0] == result[^1])
        {
            result.RemoveAt(result.Count - 1); // caller treats the polyline as an implicitly-closed polygon
        }

        return result;
    }

    private static (float X, float Y, bool OnCurve) Midpoint((float X, float Y, bool OnCurve) a, (float X, float Y, bool OnCurve) b)
        => ((a.X + b.X) / 2f, (a.Y + b.Y) / 2f, true);

    private static void AppendQuadCurve(
        List<(float X, float Y)> result,
        (float X, float Y, bool OnCurve) start,
        (float X, float Y, bool OnCurve) control,
        (float X, float Y, bool OnCurve) end)
    {
        for (var step = 1; step <= CurveSegments; step++)
        {
            var t = step / (float)CurveSegments;
            var oneMinusT = 1 - t;
            var x = oneMinusT * oneMinusT * start.X + 2 * oneMinusT * t * control.X + t * t * end.X;
            var y = oneMinusT * oneMinusT * start.Y + 2 * oneMinusT * t * control.Y + t * t * end.Y;
            result.Add((x, y));
        }
    }

    //
    // primitive big-endian readers
    //

    private ushort ReadUInt16(int offset) => BinaryPrimitives.ReadUInt16BigEndian(_data.AsSpan(offset, 2));
    private short ReadInt16(int offset) => BinaryPrimitives.ReadInt16BigEndian(_data.AsSpan(offset, 2));
    private uint ReadUInt32(int offset) => BinaryPrimitives.ReadUInt32BigEndian(_data.AsSpan(offset, 4));
}
