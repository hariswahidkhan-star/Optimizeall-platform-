using System.Collections;
using System.ComponentModel.DataAnnotations;

namespace OptimizeAll.Api.Common.Http;

/// <summary>
/// Rejects enum values that are not defined. The JSON enum converter also accepts plain numbers (e.g. <c>"status": 99</c>),
/// which would otherwise be stored as a value no screen, filter or workflow knows. Works on single values and lists.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Parameter)]
public sealed class DefinedEnumAttribute : ValidationAttribute
{
    public DefinedEnumAttribute() : base("Choose one of the listed values.")
    {
    }

    public override bool IsValid(object? value) => value switch
    {
        null => true,
        Enum e => Enum.IsDefined(e.GetType(), e),
        IEnumerable items and not string => items.Cast<object?>().All(i => i is not Enum item || Enum.IsDefined(item.GetType(), item)),
        _ => true,
    };
}
