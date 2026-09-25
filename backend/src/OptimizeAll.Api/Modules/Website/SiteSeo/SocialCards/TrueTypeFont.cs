using System.Buffers.Binary;
using System.Collections.Concurrent;

namespace OptimizeAll.Api.Modules.Website.SiteSeo.SocialCards;

/// <summary>
/// A minimal, fully managed TrueType (glyf) font reader for the social cards: character map (formats 4 and 12), advance
/// widths, pair kerning from GPOS (PairPos formats 1 and 2) and glyph outlines (simple and composite glyphs) as line and
/// quadratic segments in font units (y up). No hinting and no shaping beyond kerning: enough for Latin headlines.
/// The fonts are the OFL-licensed files embedded for the certificate PDFs (Modules/Learning/Fonts).
/// </summary>
public sealed class TrueTypeFont
{
    private readonly byte[] _data;
    private readonly Dictionary<string, int> _tables = new(StringComparer.Ordinal);
    private readonly int _locaFormat;
    private readonly int _numGlyphs;
    private readonly int _numHMetrics;
    private readonly Dictionary<int, int> _cmap = new();
    /// <summary>The 'kern' feature's PairPos subtables in lookup order; the first one that covers a pair decides.</summary>
    private readonly List<Func<int, int, int?>> _kerning = new();
    private readonly ConcurrentDictionary<int, IReadOnlyList<GlyphContour>> _outlines = new();

    public int UnitsPerEm { get; }
    public int Ascender { get; }
    public int Descender { get; }

    public TrueTypeFont(byte[] data)
    {
        _data = data;
        int numTables = U16(4);
        for (var i = 0; i < numTables; i++)
        {
            var rec = 12 + 16 * i;
            var tag = System.Text.Encoding.ASCII.GetString(data, rec, 4);
            _tables[tag] = (int)U32(rec + 8);
        }
        foreach (var required in new[] { "head", "hhea", "hmtx", "maxp", "cmap", "loca", "glyf" })
            if (!_tables.ContainsKey(required)) throw new InvalidDataException($"Font has no '{required}' table (only TrueType outlines are supported).");
        var head = _tables["head"];
        UnitsPerEm = U16(head + 18);
        _locaFormat = I16(head + 50);
        _numGlyphs = U16(_tables["maxp"] + 4);
        var hhea = _tables["hhea"];
        Ascender = I16(hhea + 4);
        Descender = I16(hhea + 6);
        _numHMetrics = U16(hhea + 34);
        ReadCmap();
        if (_tables.TryGetValue("GPOS", out var gpos)) ReadKerning(gpos);
    }

    /// <summary>Loads one of the fonts embedded in the API assembly (<c>OptimizeAll.Learning.Fonts.{file}</c>).</summary>
    public static TrueTypeFont Embedded(string file)
    {
        using var stream = typeof(TrueTypeFont).Assembly.GetManifestResourceStream("OptimizeAll.Learning.Fonts." + file)
                           ?? throw new FileNotFoundException("Embedded font not found: " + file);
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return new TrueTypeFont(ms.ToArray());
    }

    private int U16(int offset) => BinaryPrimitives.ReadUInt16BigEndian(_data.AsSpan(offset, 2));
    private short I16(int offset) => BinaryPrimitives.ReadInt16BigEndian(_data.AsSpan(offset, 2));
    private uint U32(int offset) => BinaryPrimitives.ReadUInt32BigEndian(_data.AsSpan(offset, 4));

    // ---------------------------------------------------------------- Character map

    private void ReadCmap()
    {
        var cmap = _tables["cmap"];
        int count = U16(cmap + 2);
        int best = -1, bestScore = -1;
        for (var i = 0; i < count; i++)
        {
            var rec = cmap + 4 + 8 * i;
            int platform = U16(rec), encoding = U16(rec + 2);
            var offset = cmap + (int)U32(rec + 4);
            var format = U16(offset);
            var score = (platform, encoding, format) switch
            {
                (3, 10, 12) => 4,
                (0, _, 12) => 3,
                (3, 1, 4) => 2,
                (0, _, 4) => 1,
                _ => -1,
            };
            if (score > bestScore) { bestScore = score; best = offset; }
        }
        if (best < 0) throw new InvalidDataException("Font has no Unicode character map.");
        if (U16(best) == 12)
        {
            var groups = (int)U32(best + 12);
            for (var g = 0; g < groups; g++)
            {
                var rec = best + 16 + 12 * g;
                var start = (int)U32(rec);
                var end = (int)U32(rec + 4);
                var glyph = (int)U32(rec + 8);
                for (var c = start; c <= end && c - start < 0x10000; c++) _cmap[c] = glyph + (c - start);
            }
            return;
        }
        var segX2 = U16(best + 6);
        var ends = best + 14;
        var starts = ends + segX2 + 2;
        var deltas = starts + segX2;
        var rangeOffsets = deltas + segX2;
        for (var s = 0; s < segX2 / 2; s++)
        {
            int end = U16(ends + 2 * s), start = U16(starts + 2 * s), delta = I16(deltas + 2 * s), ro = U16(rangeOffsets + 2 * s);
            for (var c = start; c <= end && c != 0xFFFF; c++)
            {
                int glyph;
                if (ro == 0) glyph = (c + delta) & 0xFFFF;
                else
                {
                    var addr = rangeOffsets + 2 * s + ro + 2 * (c - start);
                    glyph = U16(addr);
                    if (glyph != 0) glyph = (glyph + delta) & 0xFFFF;
                }
                if (glyph != 0) _cmap[c] = glyph;
            }
        }
    }

    /// <summary>The glyph for a code point (0, the "missing glyph", when the font has none).</summary>
    public int GlyphIndex(int codePoint) => _cmap.TryGetValue(codePoint, out var g) ? g : 0;

    public bool HasGlyph(int codePoint) => _cmap.ContainsKey(codePoint);

    public int AdvanceWidth(int glyph)
    {
        var hmtx = _tables["hmtx"];
        var i = Math.Min(glyph, _numHMetrics - 1);
        return U16(hmtx + 4 * i);
    }

    /// <summary>The horizontal kerning adjustment (font units) between two glyphs.</summary>
    public int Kerning(int left, int right)
    {
        foreach (var subtable in _kerning)
            if (subtable(left, right) is { } value) return value;
        return 0;
    }

    // ---------------------------------------------------------------- GPOS pair kerning

    private void ReadKerning(int gpos)
    {
        try
        {
            var lookupList = gpos + U16(gpos + 8);
            var featureList = gpos + U16(gpos + 6);
            // Lookups referenced by the 'kern' feature(s).
            var kernLookups = new HashSet<int>();
            int featureCount = U16(featureList);
            for (var f = 0; f < featureCount; f++)
            {
                var rec = featureList + 2 + 6 * f;
                if (System.Text.Encoding.ASCII.GetString(_data, rec, 4) != "kern") continue;
                var feature = featureList + U16(rec + 4);
                int lookupCount = U16(feature + 2);
                for (var l = 0; l < lookupCount; l++) kernLookups.Add(U16(feature + 4 + 2 * l));
            }
            foreach (var index in kernLookups)
            {
                var lookup = lookupList + U16(lookupList + 2 + 2 * index);
                int type = U16(lookup), subCount = U16(lookup + 4);
                for (var s = 0; s < subCount; s++)
                {
                    var sub = lookup + U16(lookup + 6 + 2 * s);
                    var subType = type;
                    if (type == 9) // extension
                    {
                        subType = U16(sub + 2);
                        sub += (int)U32(sub + 4);
                    }
                    if (subType == 2) ReadPairPos(sub);
                }
            }
        }
        catch (ArgumentOutOfRangeException)
        {
            _kerning.Clear(); // a malformed table only costs the kerning
        }
    }

    private List<int> Coverage(int offset)
    {
        var glyphs = new List<int>();
        int format = U16(offset), count = U16(offset + 2);
        if (format == 1)
            for (var i = 0; i < count; i++) glyphs.Add(U16(offset + 4 + 2 * i));
        else
            for (var i = 0; i < count; i++)
            {
                int start = U16(offset + 4 + 6 * i), end = U16(offset + 6 + 6 * i);
                for (var g = start; g <= end; g++) glyphs.Add(g);
            }
        return glyphs;
    }

    private Dictionary<int, int> ClassDef(int offset)
    {
        var classes = new Dictionary<int, int>();
        var format = U16(offset);
        if (format == 1)
        {
            int start = U16(offset + 2), count = U16(offset + 4);
            for (var i = 0; i < count; i++) classes[start + i] = U16(offset + 6 + 2 * i);
        }
        else if (format == 2)
        {
            int count = U16(offset + 2);
            for (var i = 0; i < count; i++)
            {
                int start = U16(offset + 4 + 6 * i), end = U16(offset + 6 + 6 * i), cls = U16(offset + 8 + 6 * i);
                for (var g = start; g <= end; g++) classes[g] = cls;
            }
        }
        return classes;
    }

    private static int ValueRecordSize(int format) => 2 * System.Numerics.BitOperations.PopCount((uint)format & 0xFF);

    /// <summary>The XAdvance of value record 1 (the kerning adjustment), if its format has one.</summary>
    private int XAdvance(int record, int format)
    {
        if ((format & 0x4) == 0) return 0;
        var skip = 2 * System.Numerics.BitOperations.PopCount((uint)format & 0x3);
        return I16(record + skip);
    }

    private void ReadPairPos(int sub)
    {
        int format = U16(sub), vf1 = U16(sub + 4), vf2 = U16(sub + 6);
        var coverage = Coverage(sub + U16(sub + 2));
        int size1 = ValueRecordSize(vf1), size2 = ValueRecordSize(vf2);
        if (format == 1)
        {
            var pairs = new Dictionary<(int, int), int>();
            int setCount = U16(sub + 8);
            for (var i = 0; i < setCount && i < coverage.Count; i++)
            {
                var set = sub + U16(sub + 10 + 2 * i);
                int count = U16(set);
                for (var p = 0; p < count; p++)
                {
                    var rec = set + 2 + p * (2 + size1 + size2);
                    pairs.TryAdd((coverage[i], U16(rec)), XAdvance(rec + 2, vf1));
                }
            }
            _kerning.Add((l, r) => pairs.TryGetValue((l, r), out var v) ? v : null);
        }
        else if (format == 2)
        {
            var covered = coverage.ToHashSet();
            var class1 = ClassDef(sub + U16(sub + 8));
            var class2 = ClassDef(sub + U16(sub + 10));
            int class1Count = U16(sub + 12), class2Count = U16(sub + 14);
            var values = new short[class1Count * class2Count];
            for (var i = 0; i < values.Length; i++) values[i] = (short)XAdvance(sub + 16 + i * (size1 + size2), vf1);
            _kerning.Add((l, r) =>
            {
                if (!covered.Contains(l)) return null;
                var c1 = class1.TryGetValue(l, out var a) ? a : 0;
                var c2 = class2.TryGetValue(r, out var b) ? b : 0;
                return c1 < class1Count && c2 < class2Count ? values[c1 * class2Count + c2] : null;
            });
        }
    }

    // ---------------------------------------------------------------- Outlines

    /// <summary>One closed contour: points in font units with on-curve flags (TrueType quadratic B-spline).</summary>
    public sealed record GlyphContour(IReadOnlyList<(float X, float Y, bool OnCurve)> Points);

    private int GlyphOffset(int glyph, out int length)
    {
        var loca = _tables["loca"];
        int start, end;
        if (_locaFormat == 0) { start = U16(loca + 2 * glyph) * 2; end = U16(loca + 2 * glyph + 2) * 2; }
        else { start = (int)U32(loca + 4 * glyph); end = (int)U32(loca + 4 * glyph + 4); }
        length = end - start;
        return _tables["glyf"] + start;
    }

    public IReadOnlyList<GlyphContour> Outline(int glyph) => _outlines.GetOrAdd(glyph, g => ReadOutline(g, 0));

    private IReadOnlyList<GlyphContour> ReadOutline(int glyph, int depth)
    {
        if (glyph < 0 || glyph >= _numGlyphs || depth > 8) return Array.Empty<GlyphContour>();
        var offset = GlyphOffset(glyph, out var length);
        if (length <= 0) return Array.Empty<GlyphContour>();
        int contours = I16(offset);
        return contours >= 0 ? ReadSimple(offset, contours) : ReadComposite(offset, depth);
    }

    private List<GlyphContour> ReadSimple(int offset, int contourCount)
    {
        var ends = new int[contourCount];
        for (var i = 0; i < contourCount; i++) ends[i] = U16(offset + 10 + 2 * i);
        var pointCount = contourCount == 0 ? 0 : ends[^1] + 1;
        var p = offset + 10 + 2 * contourCount;
        p += 2 + U16(p); // instructions
        var flags = new byte[pointCount];
        for (var i = 0; i < pointCount;)
        {
            var f = _data[p++];
            flags[i++] = f;
            if ((f & 8) != 0)
            {
                int repeat = _data[p++];
                while (repeat-- > 0 && i < pointCount) flags[i++] = f;
            }
        }
        var xs = new int[pointCount];
        var ys = new int[pointCount];
        var v = 0;
        for (var i = 0; i < pointCount; i++)
        {
            var f = flags[i];
            if ((f & 2) != 0) { var d = _data[p++]; v += (f & 16) != 0 ? d : -d; }
            else if ((f & 16) == 0) { v += I16(p); p += 2; }
            xs[i] = v;
        }
        v = 0;
        for (var i = 0; i < pointCount; i++)
        {
            var f = flags[i];
            if ((f & 4) != 0) { var d = _data[p++]; v += (f & 32) != 0 ? d : -d; }
            else if ((f & 32) == 0) { v += I16(p); p += 2; }
            ys[i] = v;
        }
        var result = new List<GlyphContour>(contourCount);
        var startIndex = 0;
        foreach (var end in ends)
        {
            var pts = new List<(float, float, bool)>(end - startIndex + 1);
            for (var i = startIndex; i <= end; i++) pts.Add((xs[i], ys[i], (flags[i] & 1) != 0));
            if (pts.Count > 0) result.Add(new GlyphContour(pts));
            startIndex = end + 1;
        }
        return result;
    }

    private List<GlyphContour> ReadComposite(int offset, int depth)
    {
        var result = new List<GlyphContour>();
        var p = offset + 10;
        const int ArgsAreWords = 1, ArgsAreXy = 2, HaveScale = 8, MoreComponents = 0x20, HaveXyScale = 0x40, HaveTwoByTwo = 0x80;
        int flags;
        do
        {
            flags = U16(p);
            var component = U16(p + 2);
            p += 4;
            float dx, dy;
            if ((flags & ArgsAreWords) != 0) { dx = I16(p); dy = I16(p + 2); p += 4; }
            else { dx = (sbyte)_data[p]; dy = (sbyte)_data[p + 1]; p += 2; }
            if ((flags & ArgsAreXy) == 0) { dx = 0; dy = 0; } // point matching: not used by these fonts
            float a = 1, b = 0, c = 0, d = 1;
            static float F2Dot14(short value) => value / 16384f;
            if ((flags & HaveScale) != 0) { a = d = F2Dot14(I16(p)); p += 2; }
            else if ((flags & HaveXyScale) != 0) { a = F2Dot14(I16(p)); d = F2Dot14(I16(p + 2)); p += 4; }
            else if ((flags & HaveTwoByTwo) != 0) { a = F2Dot14(I16(p)); b = F2Dot14(I16(p + 2)); c = F2Dot14(I16(p + 4)); d = F2Dot14(I16(p + 6)); p += 8; }
            foreach (var contour in ReadOutline(component, depth + 1))
                result.Add(new GlyphContour(contour.Points.Select(pt => (pt.X * a + pt.Y * c + dx, pt.X * b + pt.Y * d + dy, pt.OnCurve)).ToList()));
        }
        while ((flags & MoreComponents) != 0);
        return result;
    }
}
