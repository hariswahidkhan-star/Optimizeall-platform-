using System.Buffers.Binary;
using System.IO.Compression;
using System.Numerics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using OptimizeAll.Api.Common.Audit;
using OptimizeAll.Domain.Audit;
using OptimizeAll.Domain.Identity;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Modules.Seed;

/// <summary>
/// Simulated clock for the demo timeline. The seeder replays weeks of platform activity in chronological order and
/// hands this clock to the production services it reuses (ledger writer, settings), so timestamps, hold periods and
/// exchange-rate lookups are the ones those services would have produced at that moment.
/// </summary>
internal sealed class DemoClock(DateTime start) : TimeProvider
{
    public DateTime Now { get; set; } = DateTime.SpecifyKind(start, DateTimeKind.Utc);

    public override DateTimeOffset GetUtcNow() => new(DateTime.SpecifyKind(Now, DateTimeKind.Utc));
}

/// <summary>
/// <see cref="IAuditLogger"/> for the seed: same row shape as <see cref="AuditLogger"/>, but the actor is whoever the
/// simulation says performed the action (set with <see cref="As"/>) and the timestamp comes from <see cref="DemoClock"/>.
/// </summary>
internal sealed class DemoAuditLogger(AppDbContext db, DemoClock clock) : IAuditLogger
{
    public const string CorrelationId = "demo-seed";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
        ReferenceHandler = ReferenceHandler.IgnoreCycles,
        MaxDepth = 8,
    };

    private Guid? _actor;
    private string _actorType = "system";

    /// <summary>Sets the acting user for subsequent records (including those written by reused services).</summary>
    public DemoAuditLogger As(Guid? userId, Role? role)
    {
        _actor = userId;
        _actorType = userId is null ? "system" : role?.ToString() ?? "user";
        return this;
    }

    public void Record(string action, string entityType, object entityId, object? before = null, object? after = null, string? reason = null) =>
        db.Set<AuditLog>().Add(new AuditLog
        {
            CreatedAt = clock.Now,
            ActorUserId = _actor,
            ActorType = _actorType,
            Action = action,
            EntityType = entityType,
            EntityId = entityId.ToString() ?? string.Empty,
            BeforeJson = Serialize(before),
            AfterJson = Serialize(after),
            Reason = reason,
            IpAddress = _actor is null ? null : "10.20.0.15",
            CorrelationId = CorrelationId,
        });

    public void RecordSystem(string action, string entityType, object entityId, object? after = null, string? reason = null) =>
        db.Set<AuditLog>().Add(new AuditLog
        {
            CreatedAt = clock.Now,
            ActorType = "system",
            Action = action,
            EntityType = entityType,
            EntityId = entityId.ToString() ?? string.Empty,
            AfterJson = Serialize(after),
            Reason = reason,
            CorrelationId = CorrelationId,
        });

    private static string? Serialize(object? value) => value is null ? null : JsonSerializer.Serialize(value, JsonOptions);
}

/// <summary>
/// Encodes small but real PNG images (8-bit RGB, zlib-compressed IDAT, CRC-checked chunks) that pass the Files module's
/// <see cref="Domain.Files.ImageInspector"/> and its minimum-dimension rule. Each image looks like a phone screenshot of
/// a social post: status bar, profile row, a coloured "media" block and caption lines.
/// </summary>
internal static class DemoPng
{
    public const int Width = 320;
    public const int Height = 560;

    private static readonly (byte R, byte G, byte B)[] Palette =
    {
        (0xE1, 0x30, 0x6C), (0x00, 0x96, 0x88), (0x3F, 0x51, 0xB5), (0xFF, 0x98, 0x00), (0x8E, 0x24, 0xAA),
        (0x43, 0xA0, 0x47), (0x1E, 0x88, 0xE5), (0xF4, 0x51, 0x1E), (0x6D, 0x4C, 0x41), (0x00, 0xAC, 0xC1),
    };

    public static byte[] Screenshot(int variant)
    {
        var accent = Palette[Math.Abs(variant) % Palette.Length];
        var second = Palette[(Math.Abs(variant) / Palette.Length + 3) % Palette.Length];
        var captionLines = 3 + Math.Abs(variant) % 3;
        return Encode(Width, Height, (x, y) => Pixel(x, y, variant, accent, second, captionLines));
    }

    /// <summary>
    /// A public campaign/banner creative (hero, image asset, homepage banner): a diagonal gradient between two palette
    /// colours with a white "logo" block and a caption bar. <paramref name="variant"/> changes colours and layout.
    /// </summary>
    public static byte[] Creative(int width, int height, int variant)
    {
        var accent = Palette[Math.Abs(variant) % Palette.Length];
        var second = Palette[(Math.Abs(variant) + 4) % Palette.Length];
        var logo = Math.Min(width, height) / 5;
        var logoX = width / 4 + Math.Abs(variant) * 37 % (width / 2);
        return Encode(width, height, (x, y) =>
        {
            if (Math.Abs(x - logoX) < logo / 2 && Math.Abs(y - height / 2) < logo / 2) return (0xFF, 0xFF, 0xFF);
            if (y > height * 3 / 4 && y < height * 3 / 4 + height / 16 && x > width / 10 && x < width * 6 / 10)
                return (0xFF, 0xFF, 0xFF);
            var t = (x + y) / (double)(width + height);
            return ((byte)(accent.R + (second.R - accent.R) * t), (byte)(accent.G + (second.G - accent.G) * t),
                (byte)(accent.B + (second.B - accent.B) * t));
        });
    }

    private static byte[] Encode(int width, int height, Func<int, int, (byte R, byte G, byte B)> pixel)
    {
        var raw = new byte[(width * 3 + 1) * height];
        for (var y = 0; y < height; y++)
        {
            var row = y * (width * 3 + 1);
            raw[row] = 0; // filter: none
            for (var x = 0; x < width; x++)
            {
                var (r, g, b) = pixel(x, y);
                var i = row + 1 + x * 3;
                raw[i] = r;
                raw[i + 1] = g;
                raw[i + 2] = b;
            }
        }

        using var ms = new MemoryStream();
        ms.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });
        var ihdr = new byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(ihdr.AsSpan(0), (uint)width);
        BinaryPrimitives.WriteUInt32BigEndian(ihdr.AsSpan(4), (uint)height);
        ihdr[8] = 8;  // bit depth
        ihdr[9] = 2;  // colour type: truecolour
        ihdr[10] = 0; // compression
        ihdr[11] = 0; // filter
        ihdr[12] = 0; // interlace
        WriteChunk(ms, "IHDR", ihdr);

        using (var compressed = new MemoryStream())
        {
            using (var z = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
                z.Write(raw);
            WriteChunk(ms, "IDAT", compressed.ToArray());
        }
        WriteChunk(ms, "IEND", Array.Empty<byte>());
        return ms.ToArray();
    }

    private static (byte R, byte G, byte B) Pixel(int x, int y, int variant, (byte R, byte G, byte B) accent, (byte R, byte G, byte B) second, int captionLines)
    {
        // Status bar.
        if (y < 24) return (0x11, 0x11, 0x11);
        // Profile row: avatar circle + name bar.
        if (y is >= 36 and < 76)
        {
            var dx = x - 36;
            var dy = y - 56;
            if (dx * dx + dy * dy <= 18 * 18) return accent;
            if (x is >= 64 and < 200 && y is >= 46 and < 56) return (0x33, 0x33, 0x33);
            if (x is >= 64 and < 150 && y is >= 60 and < 68) return (0x99, 0x99, 0x99);
            return (0xFF, 0xFF, 0xFF);
        }
        // Media block with a diagonal gradient between two brand colours and a centred "logo" square.
        if (y is >= 84 and < 404)
        {
            var t = (x + (y - 84)) / (double)(Width + 320);
            if (Math.Abs(x - Width / 2) < 34 && Math.Abs(y - 244) < 34)
                return (0xFF, 0xFF, 0xFF);
            return ((byte)(accent.R + (second.R - accent.R) * t), (byte)(accent.G + (second.G - accent.G) * t),
                (byte)(accent.B + (second.B - accent.B) * t));
        }
        // Action icons.
        if (y is >= 414 and < 434 && (x % 40) is >= 14 and < 34 && x < 160) return (0x22, 0x22, 0x22);
        // Caption lines (length varies with the variant so every image hashes differently).
        for (var line = 0; line < captionLines; line++)
        {
            var top = 448 + line * 18;
            var length = 120 + (variant * 37 + line * 53) % 170;
            if (y >= top && y < top + 9 && x >= 16 && x < 16 + length) return (0x44, 0x44, 0x44);
        }
        return (0xFF, 0xFF, 0xFF);
    }

    private static void WriteChunk(Stream s, string type, byte[] data)
    {
        Span<byte> len = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(len, (uint)data.Length);
        s.Write(len);
        var typeBytes = Encoding.ASCII.GetBytes(type);
        s.Write(typeBytes);
        s.Write(data);
        var crc = Crc32(typeBytes, data);
        Span<byte> crcBytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crcBytes, crc);
        s.Write(crcBytes);
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

/// <summary>Deterministic helpers on top of a seeded <see cref="Random"/>.</summary>
internal sealed class DemoRandom(int seed)
{
    private readonly Random _random = new(seed);

    public int Next(int maxExclusive) => _random.Next(maxExclusive);
    public int Next(int min, int maxExclusive) => _random.Next(min, maxExclusive);
    public double NextDouble() => _random.NextDouble();
    public bool Chance(double p) => _random.NextDouble() < p;
    public T Pick<T>(IReadOnlyList<T> items) => items[_random.Next(items.Count)];

    public TimeSpan Hours(double min, double max) => TimeSpan.FromMinutes(Math.Round((min + (max - min) * _random.NextDouble()) * 60));

    public DateTime Between(DateTime from, DateTime to) =>
        to <= from ? from : from.AddSeconds(Math.Floor((to - from).TotalSeconds * _random.NextDouble()));

    public string Digits(int length)
    {
        var sb = new StringBuilder(length);
        sb.Append((char)('1' + _random.Next(9)));
        for (var i = 1; i < length; i++) sb.Append((char)('0' + _random.Next(10)));
        return sb.ToString();
    }

    public string Chars(string alphabet, int length)
    {
        var sb = new StringBuilder(length);
        for (var i = 0; i < length; i++) sb.Append(alphabet[_random.Next(alphabet.Length)]);
        return sb.ToString();
    }

    /// <summary>64-char lower-case hex (the shape of the platform's salted SHA-256 hashes).</summary>
    public string Hash64() => Chars("0123456789abcdef", 64);
}

internal static class DemoIban
{
    /// <summary>Builds a checksum-valid IBAN (ISO 13616 mod-97) from a country code and BBAN.</summary>
    public static string Create(string country, string bban)
    {
        var rearranged = bban + country + "00";
        var digits = new StringBuilder();
        foreach (var c in rearranged)
            digits.Append(char.IsDigit(c) ? c.ToString() : (c - 'A' + 10).ToString());
        var check = 98 - (int)(BigInteger.Parse(digits.ToString()) % 97);
        return $"{country}{check:00}{bban}";
    }
}
