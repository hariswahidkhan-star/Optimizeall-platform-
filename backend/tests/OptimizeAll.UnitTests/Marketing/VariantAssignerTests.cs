using OptimizeAll.Domain.Marketing;

namespace OptimizeAll.UnitTests.Marketing;

public sealed class VariantAssignerTests
{
    private static readonly Guid Experiment = Guid.Parse("0192a000-0000-7000-8000-000000000001");

    [Fact]
    public void Assignment_is_deterministic_and_independent_of_variant_order()
    {
        var ab = new[] { new WeightedVariant("A", 50), new WeightedVariant("B", 50) };
        var ba = new[] { new WeightedVariant("B", 50), new WeightedVariant("A", 50) };
        for (var i = 0; i < 200; i++)
        {
            var subject = $"user:{i}";
            var first = VariantAssigner.Assign(Experiment, subject, ab);
            Assert.Equal(first, VariantAssigner.Assign(Experiment, subject, ab));
            Assert.Equal(first, VariantAssigner.Assign(Experiment, subject, ba));
        }
    }

    [Fact]
    public void Bucket_matches_the_documented_formula()
    {
        var hash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes($"{Experiment:D}:user:42"));
        var expected = System.Buffers.Binary.BinaryPrimitives.ReadUInt64BigEndian(hash.AsSpan(0, 8)) % 100;
        Assert.Equal(expected, VariantAssigner.Bucket(Experiment, "user:42", 100));
        // Bucket [0, 50) -> A, [50, 100) -> B for a 50/50 split.
        var variants = new[] { new WeightedVariant("A", 50), new WeightedVariant("B", 50) };
        Assert.Equal(expected < 50 ? "A" : "B", VariantAssigner.Assign(Experiment, "user:42", variants));
    }

    [Theory]
    [InlineData(new[] { 50, 50 })]
    [InlineData(new[] { 20, 30, 50 })]
    [InlineData(new[] { 10, 10, 10, 70 })]
    [InlineData(new[] { 1, 99 })]
    public void Distribution_over_10k_subjects_follows_weights(int[] weights)
    {
        var keys = new[] { "A", "B", "C", "D" };
        var variants = weights.Select((w, i) => new WeightedVariant(keys[i], w)).ToList();
        const int n = 10_000;
        var counts = keys.Take(weights.Length).ToDictionary(k => k, _ => 0);
        // Deterministic subject keys make the test reproducible.
        for (var i = 0; i < n; i++)
            counts[VariantAssigner.Assign(Experiment, $"user:{i:D6}", variants)]++;

        var total = weights.Sum();
        foreach (var v in variants)
        {
            var share = v.Weight / (double)total;
            var expected = n * share;
            // Tolerance: ±4 binomial standard deviations (+1 for tiny weights).
            var sd = Math.Sqrt(n * share * (1 - share));
            Assert.InRange(counts[v.Key], expected - 4 * sd - 1, expected + 4 * sd + 1);
        }
    }

    [Fact]
    public void Different_experiments_assign_independently()
    {
        var other = Guid.Parse("0192a000-0000-7000-8000-000000000002");
        var variants = new[] { new WeightedVariant("A", 50), new WeightedVariant("B", 50) };
        var same = Enumerable.Range(0, 2000).Count(i =>
            VariantAssigner.Assign(Experiment, $"visitor:{i}", variants) == VariantAssigner.Assign(other, $"visitor:{i}", variants));
        Assert.InRange(same, 850, 1150); // ~50% agreement expected for independent assignment
    }

    [Fact]
    public void Invalid_input_is_rejected()
    {
        Assert.Throws<ArgumentException>(() => VariantAssigner.Assign(Experiment, "user:1", Array.Empty<WeightedVariant>()));
        Assert.Throws<ArgumentException>(() => VariantAssigner.Assign(Experiment, "user:1", new[] { new WeightedVariant("A", 0) }));
        Assert.Throws<ArgumentException>(() => VariantAssigner.Assign(Experiment, " ", new[] { new WeightedVariant("A", 1) }));
    }

    [Fact]
    public void Subject_keys_have_the_documented_format()
    {
        var id = Guid.Parse("0192a000-0000-7000-8000-00000000000a");
        Assert.Equal("user:0192a000-0000-7000-8000-00000000000a", VariantAssigner.UserSubject(id));
        Assert.Equal("visitor:abc", VariantAssigner.VisitorSubject("abc"));
    }
}
