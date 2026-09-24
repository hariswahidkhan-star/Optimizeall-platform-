using System.ComponentModel.DataAnnotations;
using System.Reflection;

namespace OptimizeAll.IntegrationTests.ApiContract;

/// <summary>Static checks of the validation metadata of the API's request types (no host needed).</summary>
public sealed class ValidationMetadataTests
{
    private static IEnumerable<PropertyInfo> ApiProperties() =>
        typeof(Program).Assembly.GetTypes()
            .Where(t => t.Namespace?.StartsWith("OptimizeAll.Api", StringComparison.Ordinal) == true)
            .SelectMany(t => t.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly));

    /// <summary>
    /// <c>[Range(0, 1000)]</c> (the Int32 overload) converts the value with <c>Convert.ToInt32</c>: on a decimal or long
    /// property a large value throws <c>OverflowException</c> inside model validation (a 500 instead of a 400), and fractions
    /// are rounded before the comparison. Non-Int32 properties use <c>[Range(typeof(decimal), "0", "1000")]</c> or the double overload.
    /// </summary>
    [Fact]
    public void Int32_ranges_only_guard_Int32_properties()
    {
        static bool Int32Only(Type type) => (Nullable.GetUnderlyingType(type) ?? type) is var t && (t == typeof(int) || t == typeof(short) || t == typeof(byte));
        var properties = ApiProperties()
            .Where(p => p.GetCustomAttribute<RangeAttribute>() is { OperandType: var t } && t == typeof(int) && !Int32Only(p.PropertyType))
            .Select(p => $"{p.DeclaringType!.FullName}.{p.Name} ({p.PropertyType.Name})");
        // Record primary-constructor parameters carry their own validation attributes.
        var parameters = typeof(Program).Assembly.GetTypes()
            .Where(t => t.Namespace?.StartsWith("OptimizeAll.Api", StringComparison.Ordinal) == true)
            .SelectMany(t => t.GetConstructors().SelectMany(c => c.GetParameters()))
            .Where(p => p.GetCustomAttribute<RangeAttribute>() is { OperandType: var t } && t == typeof(int) && !Int32Only(p.ParameterType))
            .Select(p => $"{p.Member.DeclaringType!.FullName}({p.Name}: {p.ParameterType.Name})");
        var offenders = properties.Concat(parameters).Distinct().OrderBy(x => x).ToList();
        Assert.True(offenders.Count == 0, "Int32 [Range] on non-Int32 properties:\n" + string.Join('\n', offenders));
    }
}
