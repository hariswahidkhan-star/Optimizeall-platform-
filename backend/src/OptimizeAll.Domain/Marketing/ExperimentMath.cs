namespace OptimizeAll.Domain.Marketing;

/// <summary>Result of a two-sided two-proportion z-test. <see cref="PValue"/> is null when the test is undefined.</summary>
public sealed record ProportionTest(double? Z, double? PValue);

/// <summary>Pure statistics used by experiment results.</summary>
public static class ExperimentMath
{
    /// <summary>Minimum assignments per variant before a difference may be called significant.</summary>
    public const int MinAssignmentsForConclusion = 100;

    public const double SignificanceLevel = 0.05;

    /// <summary>
    /// Two-sided two-proportion z-test with pooled variance comparing x1/n1 (control) with x2/n2 (variant).
    /// z = (p2 − p1) / sqrt(p(1−p)(1/n1 + 1/n2)), p = (x1+x2)/(n1+n2); p-value = 2·(1 − Φ(|z|)).
    /// Undefined (nulls) when either group is empty or the pooled proportion is 0 or 1.
    /// </summary>
    public static ProportionTest TwoProportionZTest(long x1, long n1, long x2, long n2)
    {
        if (n1 <= 0 || n2 <= 0) return new ProportionTest(null, null);
        if (x1 < 0 || x2 < 0 || x1 > n1 || x2 > n2) throw new ArgumentOutOfRangeException(nameof(x1), "Successes must be between 0 and n.");

        var p1 = (double)x1 / n1;
        var p2 = (double)x2 / n2;
        var pooled = (double)(x1 + x2) / (n1 + n2);
        var se = Math.Sqrt(pooled * (1 - pooled) * (1.0 / n1 + 1.0 / n2));
        if (se == 0 || double.IsNaN(se)) return new ProportionTest(null, null);

        var z = (p2 - p1) / se;
        var p = 2 * (1 - NormalCdf(Math.Abs(z)));
        return new ProportionTest(z, Math.Clamp(p, 0, 1));
    }

    /// <summary>Standard normal CDF Φ(x) = ½·erfc(−x/√2).</summary>
    public static double NormalCdf(double x) => 0.5 * Erfc(-x / Math.Sqrt(2));

    /// <summary>
    /// Complementary error function (Chebyshev fit from Numerical Recipes, fractional error below 1.2e-7 everywhere).
    /// </summary>
    public static double Erfc(double x)
    {
        var z = Math.Abs(x);
        var t = 1.0 / (1.0 + 0.5 * z);
        var ans = t * Math.Exp(-z * z - 1.26551223 + t * (1.00002368 + t * (0.37409196 + t * (0.09678418 +
            t * (-0.18628806 + t * (0.27886807 + t * (-1.13520398 + t * (1.48851587 +
            t * (-0.82215223 + t * 0.17087277)))))))));
        return x >= 0 ? ans : 2.0 - ans;
    }

    /// <summary>Safe ratio: null when the denominator is zero.</summary>
    public static double? Rate(long numerator, long denominator) =>
        denominator == 0 ? null : (double)numerator / denominator;
}
