using OptimizeAll.Domain.Marketing;

namespace OptimizeAll.UnitTests.Marketing;

public sealed class ExperimentMathTests
{
    // Reference values: pooled two-proportion z-test with the exact erfc (computed with Python's math.erfc).
    [Theory]
    [InlineData(100, 1000, 130, 1000, 2.102740605622114, 0.03548845046647473)]
    [InlineData(45, 500, 60, 500, 1.547337646055907, 0.1217818484258567)]
    [InlineData(30, 120, 45, 118, 2.1809482575405705, 0.029187243796399153)]
    [InlineData(50, 200, 80, 200, 3.202563076101743, 0.001362104671584203)]
    [InlineData(200, 1000, 200, 1000, 0.0, 1.0)]
    public void Two_proportion_z_test_matches_reference_values(long x1, long n1, long x2, long n2, double z, double p)
    {
        var result = ExperimentMath.TwoProportionZTest(x1, n1, x2, n2);
        Assert.NotNull(result.Z);
        Assert.NotNull(result.PValue);
        Assert.Equal(z, result.Z!.Value, 6);
        Assert.Equal(p, result.PValue!.Value, 6);
    }

    [Fact]
    public void Z_test_is_symmetric_in_sign()
    {
        var up = ExperimentMath.TwoProportionZTest(100, 1000, 130, 1000);
        var down = ExperimentMath.TwoProportionZTest(130, 1000, 100, 1000);
        Assert.Equal(-up.Z!.Value, down.Z!.Value, 9);
        Assert.Equal(up.PValue!.Value, down.PValue!.Value, 9);
    }

    [Theory]
    [InlineData(0, 0, 5, 10)]
    [InlineData(0, 100, 0, 100)]
    [InlineData(100, 100, 100, 100)]
    public void Undefined_cases_return_null(long x1, long n1, long x2, long n2)
    {
        var result = ExperimentMath.TwoProportionZTest(x1, n1, x2, n2);
        Assert.Null(result.PValue);
        Assert.Null(result.Z);
    }

    [Fact]
    public void Successes_above_trials_are_rejected() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => ExperimentMath.TwoProportionZTest(11, 10, 1, 10));

    [Fact]
    public void Normal_cdf_known_points()
    {
        Assert.Equal(0.5, ExperimentMath.NormalCdf(0), 7);
        Assert.Equal(0.9750021048517795, ExperimentMath.NormalCdf(1.96), 6);
        Assert.Equal(0.15865525393145707, ExperimentMath.NormalCdf(-1), 6);
    }

    [Fact]
    public void Rate_handles_zero_denominator()
    {
        Assert.Null(ExperimentMath.Rate(1, 0));
        Assert.Equal(0.25, ExperimentMath.Rate(1, 4));
    }
}
