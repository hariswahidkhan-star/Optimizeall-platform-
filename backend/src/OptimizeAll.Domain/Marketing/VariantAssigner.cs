using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace OptimizeAll.Domain.Marketing;

/// <summary>A variant candidate for assignment: its key and relative weight (≥ 1).</summary>
public sealed record WeightedVariant(string Key, int Weight);

/// <summary>
/// Deterministic, sticky A/B assignment. The same subject always lands in the same variant of an experiment
/// (as long as weights do not change), independent of request order or server instance:
/// bucket = first 8 bytes (big-endian) of SHA-256("{experimentId}:{subjectKey}") mod Σweights,
/// then the variant whose cumulative weight range contains the bucket (variants ordered by key).
/// </summary>
public static class VariantAssigner
{
    public static ulong Bucket(Guid experimentId, string subjectKey, ulong modulus)
    {
        if (modulus == 0) throw new ArgumentOutOfRangeException(nameof(modulus));
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{experimentId:D}:{subjectKey}"));
        return BinaryPrimitives.ReadUInt64BigEndian(hash.AsSpan(0, 8)) % modulus;
    }

    /// <summary>Returns the key of the variant for <paramref name="subjectKey"/>.</summary>
    public static string Assign(Guid experimentId, string subjectKey, IReadOnlyCollection<WeightedVariant> variants)
    {
        if (variants.Count == 0) throw new ArgumentException("At least one variant is required.", nameof(variants));
        if (variants.Any(v => v.Weight < 1)) throw new ArgumentException("Weights must be at least 1.", nameof(variants));
        if (string.IsNullOrWhiteSpace(subjectKey)) throw new ArgumentException("A subject key is required.", nameof(subjectKey));

        var ordered = variants.OrderBy(v => v.Key, StringComparer.Ordinal).ToList();
        var total = (ulong)ordered.Sum(v => (long)v.Weight);
        var bucket = Bucket(experimentId, subjectKey, total);

        ulong cumulative = 0;
        foreach (var variant in ordered)
        {
            cumulative += (ulong)variant.Weight;
            if (bucket < cumulative) return variant.Key;
        }
        return ordered[^1].Key;
    }

    public static string UserSubject(Guid userId) => $"user:{userId:D}";

    public static string VisitorSubject(string visitorHash) => $"visitor:{visitorHash}";
}
