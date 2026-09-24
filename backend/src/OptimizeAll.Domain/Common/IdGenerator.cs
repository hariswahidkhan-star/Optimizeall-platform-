using System.Security.Cryptography;

namespace OptimizeAll.Domain.Common;

/// <summary>
/// Generates RFC 9562 UUIDv7 values (millisecond timestamp prefix + randomness) so primary keys
/// stay roughly insertion-ordered in MySQL B-tree indexes while remaining unguessable.
/// </summary>
public static class IdGenerator
{
    public static Guid NewId() => NewId(DateTimeOffset.UtcNow);

    public static Guid NewId(DateTimeOffset timestamp)
    {
        Span<byte> bytes = stackalloc byte[16];
        RandomNumberGenerator.Fill(bytes);

        long ms = timestamp.ToUnixTimeMilliseconds();
        bytes[0] = (byte)(ms >> 40);
        bytes[1] = (byte)(ms >> 32);
        bytes[2] = (byte)(ms >> 24);
        bytes[3] = (byte)(ms >> 16);
        bytes[4] = (byte)(ms >> 8);
        bytes[5] = (byte)ms;
        bytes[6] = (byte)((bytes[6] & 0x0F) | 0x70); // version 7
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80); // RFC variant

        return new Guid(bytes, bigEndian: true);
    }

    /// <summary>
    /// The smallest id <see cref="NewId(DateTimeOffset)"/> can return for <paramref name="timestamp"/> (timestamp prefix,
    /// all other bits zero). Every id generated before that millisecond sorts below it — as a Guid and as its text form,
    /// which is how both database providers store and compare it — so <c>Id &lt; LowerBound(t)</c> is a primary-key range
    /// over rows created before <paramref name="timestamp"/> (housekeeping uses it to avoid a time index on large tables).
    /// </summary>
    public static Guid LowerBound(DateTimeOffset timestamp)
    {
        Span<byte> bytes = stackalloc byte[16];
        long ms = Math.Max(0, timestamp.ToUnixTimeMilliseconds());
        bytes[0] = (byte)(ms >> 40);
        bytes[1] = (byte)(ms >> 32);
        bytes[2] = (byte)(ms >> 24);
        bytes[3] = (byte)(ms >> 16);
        bytes[4] = (byte)(ms >> 8);
        bytes[5] = (byte)ms;
        return new Guid(bytes, bigEndian: true);
    }
}
