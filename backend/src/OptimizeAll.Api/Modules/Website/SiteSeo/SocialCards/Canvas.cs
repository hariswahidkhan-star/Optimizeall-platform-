using System.Buffers.Binary;
using System.IO.Compression;

namespace OptimizeAll.Api.Modules.Website.SiteSeo.SocialCards;

/// <summary>An sRGB colour with alpha (0–1).</summary>
public readonly record struct Rgba(byte R, byte G, byte B, float A = 1f)
{
    public static Rgba Hex(string hex, float alpha = 1f)
    {
        var h = hex.TrimStart('#');
        return new Rgba(Convert.ToByte(h[..2], 16), Convert.ToByte(h[2..4], 16), Convert.ToByte(h[4..6], 16), alpha);
    }

    public Rgba WithAlpha(float alpha) => this with { A = alpha };

    public static Rgba Lerp(Rgba a, Rgba b, float t) => new(
        (byte)Math.Round(a.R + (b.R - a.R) * t), (byte)Math.Round(a.G + (b.G - a.G) * t), (byte)Math.Round(a.B + (b.B - a.B) * t), a.A + (b.A - a.A) * t);
}

/// <summary>
/// A vector path of closed polygons in pixel coordinates (y down). Curves are flattened on the way in, so the path is
/// only line segments; <see cref="Canvas.Fill"/> rasterizes it with exact-area anti-aliasing (non-zero coverage, clamped).
/// </summary>
public sealed class VectorPath
{
    internal readonly List<(float X0, float Y0, float X1, float Y1)> Lines = new();
    private float _sx, _sy, _cx, _cy;
    private bool _open;

    public void MoveTo(float x, float y)
    {
        Close();
        _sx = _cx = x; _sy = _cy = y; _open = true;
    }

    public void LineTo(float x, float y)
    {
        if (x != _cx || y != _cy) Lines.Add((_cx, _cy, x, y));
        _cx = x; _cy = y;
    }

    public void QuadTo(float x1, float y1, float x2, float y2)
    {
        // Enough steps that each chord deviates < ~0.1 px from the curve.
        var dd = MathF.Abs(_cx - 2 * x1 + x2) + MathF.Abs(_cy - 2 * y1 + y2);
        var steps = Math.Clamp((int)MathF.Ceiling(MathF.Sqrt(dd * 2.5f)), 1, 64);
        float x0 = _cx, y0 = _cy;
        for (var i = 1; i <= steps; i++)
        {
            var t = i / (float)steps;
            var mt = 1 - t;
            LineTo(mt * mt * x0 + 2 * mt * t * x1 + t * t * x2, mt * mt * y0 + 2 * mt * t * y1 + t * t * y2);
        }
    }

    public void Close()
    {
        if (_open) LineTo(_sx, _sy);
        _open = false;
    }

    public void Rect(float x, float y, float w, float h)
    {
        MoveTo(x, y); LineTo(x + w, y); LineTo(x + w, y + h); LineTo(x, y + h); Close();
    }

    public void RoundedRect(float x, float y, float w, float h, float r)
    {
        r = Math.Min(r, Math.Min(w, h) / 2);
        MoveTo(x + r, y);
        LineTo(x + w - r, y); Arc(x + w - r, y + r, r, -MathF.PI / 2, 0, true);
        LineTo(x + w, y + h - r); Arc(x + w - r, y + h - r, r, 0, MathF.PI / 2, true);
        LineTo(x + r, y + h); Arc(x + r, y + h - r, r, MathF.PI / 2, MathF.PI, true);
        LineTo(x, y + r); Arc(x + r, y + r, r, MathF.PI, 1.5f * MathF.PI, true);
        Close();
    }

    /// <summary>Appends an arc (angles in radians, y down) as line segments from the current point.</summary>
    public void Arc(float cx, float cy, float r, float from, float to, bool connect)
    {
        var steps = Math.Clamp((int)(MathF.Abs(to - from) * r / 3), 8, 256);
        for (var i = connect ? 1 : 0; i <= steps; i++)
        {
            var a = from + (to - from) * i / steps;
            var x = cx + r * MathF.Cos(a);
            var y = cy + r * MathF.Sin(a);
            if (i == 0) MoveTo(x, y); else LineTo(x, y);
        }
    }

    /// <summary>A ring (annulus): outer circle clockwise, inner counter-clockwise, so the middle stays empty.</summary>
    public void Ring(float cx, float cy, float radius, float stroke)
    {
        float outer = radius + stroke / 2, inner = radius - stroke / 2;
        Arc(cx, cy, outer, 0, 2 * MathF.PI, false); Close();
        Arc(cx, cy, inner, 2 * MathF.PI, 0, false); Close();
    }

    /// <summary>A stroked circular arc as a closed band (for the logo's accent arc).</summary>
    public void ArcBand(float cx, float cy, float radius, float stroke, float from, float to)
    {
        float outer = radius + stroke / 2, inner = radius - stroke / 2;
        Arc(cx, cy, outer, from, to, false);
        Arc(cx, cy, inner, to, from, true);
        Close();
    }

    public void Circle(float cx, float cy, float r)
    {
        Arc(cx, cy, r, 0, 2 * MathF.PI, false);
        Close();
    }
}

/// <summary>
/// An RGB raster (no alpha) with anti-aliased polygon filling and PNG output. Fully managed, no native dependencies. The
/// fill uses the signed-area accumulation technique (as in font-rs / stb_truetype 2): each edge adds its exact coverage
/// to an accumulation buffer, and a running sum per row gives every pixel's covered area.
/// </summary>
public sealed class Canvas
{
    public int Width { get; }
    public int Height { get; }
    private readonly byte[] _rgb;

    public Canvas(int width, int height)
    {
        Width = width;
        Height = height;
        _rgb = new byte[width * height * 3];
    }

    /// <summary>Fills the whole canvas with a diagonal two-stop gradient (top-left → bottom-right).</summary>
    public void DiagonalGradient(Rgba from, Rgba to)
    {
        var span = (float)(Width + Height);
        for (var y = 0; y < Height; y++)
        {
            var row = y * Width * 3;
            for (var x = 0; x < Width; x++)
            {
                var c = Rgba.Lerp(from, to, (x + y) / span);
                var i = row + x * 3;
                _rgb[i] = c.R; _rgb[i + 1] = c.G; _rgb[i + 2] = c.B;
            }
        }
    }

    /// <summary>A soft radial glow (colour fading from <paramref name="color"/>'s alpha at the centre to 0 at the radius).</summary>
    public void Glow(float cx, float cy, float radius, Rgba color)
    {
        int x0 = Math.Max(0, (int)(cx - radius)), x1 = Math.Min(Width, (int)(cx + radius) + 1);
        int y0 = Math.Max(0, (int)(cy - radius)), y1 = Math.Min(Height, (int)(cy + radius) + 1);
        for (var y = y0; y < y1; y++)
            for (var x = x0; x < x1; x++)
            {
                var d = MathF.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy)) / radius;
                if (d >= 1) continue;
                var fall = (1 - d) * (1 - d);
                Blend(x, y, color, color.A * fall);
            }
    }

    private void Blend(int x, int y, Rgba c, float alpha)
    {
        if (alpha <= 0) return;
        if (alpha > 1) alpha = 1;
        var i = (y * Width + x) * 3;
        _rgb[i] = (byte)(_rgb[i] + (c.R - _rgb[i]) * alpha + 0.5f);
        _rgb[i + 1] = (byte)(_rgb[i + 1] + (c.G - _rgb[i + 1]) * alpha + 0.5f);
        _rgb[i + 2] = (byte)(_rgb[i + 2] + (c.B - _rgb[i + 2]) * alpha + 0.5f);
    }

    /// <summary>Fills <paramref name="path"/> with <paramref name="color"/> (anti-aliased; parts outside the canvas are clipped).</summary>
    public void Fill(VectorPath path, Rgba color)
    {
        path.Close();
        if (path.Lines.Count == 0) return;
        float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
        foreach (var (x0, y0, x1, y1) in path.Lines)
        {
            minX = Math.Min(minX, Math.Min(x0, x1)); maxX = Math.Max(maxX, Math.Max(x0, x1));
            minY = Math.Min(minY, Math.Min(y0, y1)); maxY = Math.Max(maxY, Math.Max(y0, y1));
        }
        // The accumulation window: the path's bounds clipped to the canvas.
        var ox = Math.Max(0, (int)MathF.Floor(minX));
        var oy = Math.Max(0, (int)MathF.Floor(minY));
        var ex = Math.Min(Width, (int)MathF.Ceiling(maxX));
        var ey = Math.Min(Height, (int)MathF.Ceiling(maxY));
        if (ex <= ox || ey <= oy) return;
        var w = ex - ox;
        var h = ey - oy;
        var stride = w + 2;
        var acc = new float[stride * h + 2];
        foreach (var (x0, y0, x1, y1) in path.Lines)
        {
            // Split at the window's left and right edges; a piece outside becomes a vertical edge on the boundary, which
            // keeps the covered area of every pixel inside the window exact (coverage accumulates rightward).
            float ax = x0 - ox, ay = y0 - oy, bx = x1 - ox, by = y1 - oy;
            var cuts = new List<float> { 0f, 1f };
            foreach (var edge in new float[] { 0, w })
                if ((ax - edge) * (bx - edge) < 0) cuts.Add((edge - ax) / (bx - ax));
            cuts.Sort();
            for (var i = 0; i + 1 < cuts.Count; i++)
            {
                float t0 = cuts[i], t1 = cuts[i + 1];
                DrawLine(acc, stride, w, h,
                    Math.Clamp(ax + (bx - ax) * t0, 0, w), ay + (by - ay) * t0,
                    Math.Clamp(ax + (bx - ax) * t1, 0, w), ay + (by - ay) * t1);
            }
        }
        for (var y = 0; y < h; y++)
        {
            var sum = 0f;
            var row = y * stride;
            for (var x = 0; x < w; x++)
            {
                sum += acc[row + x];
                var coverage = MathF.Min(1f, MathF.Abs(sum));
                if (coverage > 0.002f) Blend(ox + x, oy + y, color, coverage * color.A);
            }
        }
    }

    private static void DrawLine(float[] a, int stride, int w, int h, float px0, float py0, float px1, float py1)
    {
        if (MathF.Abs(py0 - py1) <= float.Epsilon) return;
        float dir;
        float x0p, y0p, x1p, y1p;
        if (py0 < py1) { dir = 1; x0p = px0; y0p = py0; x1p = px1; y1p = py1; }
        else { dir = -1; x0p = px1; y0p = py1; x1p = px0; y1p = py0; }
        var dxdy = (x1p - x0p) / (y1p - y0p);
        var x = x0p;
        if (y0p < 0) x -= y0p * dxdy;
        var yStart = Math.Max(0, (int)y0p);
        var yEnd = Math.Min(h, (int)MathF.Ceiling(y1p));
        for (var y = yStart; y < yEnd; y++)
        {
            var line = y * stride;
            var dy = MathF.Min(y + 1, y1p) - MathF.Max(y, y0p);
            var xnext = x + dxdy * dy;
            var d = dy * dir;
            float xa = Math.Clamp(x < xnext ? x : xnext, 0, w), xb = Math.Clamp(x < xnext ? xnext : x, 0, w);
            var xaFloor = MathF.Floor(xa);
            var xai = (int)xaFloor;
            var xbCeil = MathF.Ceiling(xb);
            var xbi = (int)xbCeil;
            if (xbi <= xai + 1)
            {
                var xmf = Math.Clamp(0.5f * (xa + xb) - xaFloor, 0, 1);
                a[line + xai] += d - d * xmf;
                a[line + xai + 1] += d * xmf;
            }
            else
            {
                var s = 1f / (xb - xa);
                var xaf = xa - xaFloor;
                var a0 = 0.5f * s * (1 - xaf) * (1 - xaf);
                var xbf = xb - xbCeil + 1;
                var am = 0.5f * s * xbf * xbf;
                a[line + xai] += d * a0;
                if (xbi == xai + 2)
                    a[line + xai + 1] += d * (1 - a0 - am);
                else
                {
                    var a1 = s * (1.5f - xaf);
                    a[line + xai + 1] += d * (a1 - a0);
                    for (var xi = xai + 2; xi < xbi - 1; xi++) a[line + xi] += d * s;
                    var a2 = a1 + (xbi - xai - 3) * s;
                    a[line + xbi - 1] += d * (1 - a2 - am);
                }
                a[line + xbi] += d * am;
            }
            x = xnext;
        }
    }

    public byte[] Pixel(int x, int y)
    {
        var i = (y * Width + x) * 3;
        return new[] { _rgb[i], _rgb[i + 1], _rgb[i + 2] };
    }

    /// <summary>Encodes the canvas as an 8-bit RGB PNG (per-row adaptive filter, zlib).</summary>
    public byte[] ToPng()
    {
        using var output = new MemoryStream();
        output.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });
        var ihdr = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(ihdr, Width);
        BinaryPrimitives.WriteInt32BigEndian(ihdr.AsSpan(4), Height);
        ihdr[8] = 8; // bit depth
        ihdr[9] = 2; // colour type: truecolour
        Chunk(output, "IHDR", ihdr);

        using var raw = new MemoryStream();
        using (var z = new ZLibStream(raw, CompressionLevel.Optimal, leaveOpen: true))
        {
            var stride = Width * 3;
            var candidates = new byte[5][];
            for (var f = 0; f < 5; f++) candidates[f] = new byte[stride];
            var zero = new byte[stride];
            for (var y = 0; y < Height; y++)
            {
                var row = new ReadOnlySpan<byte>(_rgb, y * stride, stride);
                var prev = y == 0 ? zero : new ReadOnlySpan<byte>(_rgb, (y - 1) * stride, stride);
                // Sub, Up, Average and Paeth (None is never better on these images); pick the smallest sum of |residuals|.
                long sSub = 0, sUp = 0, sAvg = 0, sPaeth = 0;
                var sub = candidates[1]; var upF = candidates[2]; var avg = candidates[3]; var paeth = candidates[4];
                for (var i = 0; i < stride; i++)
                {
                    int cur = row[i], left = i >= 3 ? row[i - 3] : 0, up = prev[i], upLeft = i >= 3 ? prev[i - 3] : 0;
                    byte v;
                    v = (byte)(cur - left); sub[i] = v; sSub += v < 128 ? v : 256 - v;
                    v = (byte)(cur - up); upF[i] = v; sUp += v < 128 ? v : 256 - v;
                    v = (byte)(cur - ((left + up) >> 1)); avg[i] = v; sAvg += v < 128 ? v : 256 - v;
                    v = (byte)(cur - Paeth(left, up, upLeft)); paeth[i] = v; sPaeth += v < 128 ? v : 256 - v;
                }
                var best = 1; var bestScore = sSub;
                if (sUp < bestScore) { best = 2; bestScore = sUp; }
                if (sAvg < bestScore) { best = 3; bestScore = sAvg; }
                if (sPaeth < bestScore) best = 4;
                z.WriteByte((byte)best);
                z.Write(candidates[best]);
            }
        }
        Chunk(output, "IDAT", raw.ToArray());
        Chunk(output, "IEND", Array.Empty<byte>());
        return output.ToArray();
    }

    private static int Paeth(int a, int b, int c)
    {
        var p = a + b - c;
        int pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
        return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
    }

    private static void Chunk(Stream output, string type, byte[] data)
    {
        Span<byte> len = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(len, data.Length);
        output.Write(len);
        var typeBytes = System.Text.Encoding.ASCII.GetBytes(type);
        output.Write(typeBytes);
        output.Write(data);
        var crc = Crc32(typeBytes, data);
        Span<byte> crcBytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crcBytes, crc);
        output.Write(crcBytes);
    }

    private static readonly uint[] CrcTable = BuildCrcTable();

    private static uint[] BuildCrcTable()
    {
        var table = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            var c = n;
            for (var k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            table[n] = c;
        }
        return table;
    }

    private static uint Crc32(byte[] type, byte[] data)
    {
        var c = 0xFFFFFFFFu;
        foreach (var b in type) c = CrcTable[(c ^ b) & 0xFF] ^ (c >> 8);
        foreach (var b in data) c = CrcTable[(c ^ b) & 0xFF] ^ (c >> 8);
        return c ^ 0xFFFFFFFFu;
    }
}
