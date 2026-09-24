using System.Collections;
using System.ComponentModel.DataAnnotations;
using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ModelBinding.Metadata;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;

namespace OptimizeAll.Api.Common.Http;

/// <summary>
/// Model-validation rules every request value obeys, whatever DTO or parameter carries it (registered once in
/// <c>AddControllers</c>, so handlers can rely on them and a hostile value is a 400 for its field instead of an exception):
/// <list type="bullet">
/// <item>dates (<see cref="DateTime"/>, <see cref="DateTimeOffset"/>, <see cref="DateOnly"/>) lie between 1900 and 2200: date
/// arithmetic on <c>0001-01-01</c> or <c>9999-12-31</c> (a range end plus a day, a week start) overflows. An omitted,
/// non-nullable DTO property (its default value) is left to the DTO's own rules;</item>
/// <item>lists contain no <c>null</c> items unless the item type is declared nullable (<c>List&lt;string?&gt;</c>);</item>
/// <item>a <c>[Required]</c> <see cref="JsonElement"/> is present and not <c>null</c> (<c>[Required]</c> alone accepts the
/// default, undefined element of an omitted property).</item>
/// </list>
/// </summary>
public sealed class RequestValueValidatorProvider : IMetadataBasedModelValidatorProvider
{
    public const int MinYear = 1900;
    public const int MaxYear = 2200;

    public bool HasValidators(Type modelType, IList<object> validatorMetadata) =>
        IsDate(modelType) || IsList(modelType) || (IsJsonElement(modelType) && validatorMetadata.OfType<RequiredAttribute>().Any());

    public void CreateValidators(ModelValidatorProviderContext context)
    {
        var metadata = context.ModelMetadata;
        var type = metadata.ModelType;
        IModelValidator? validator = null;
        if (IsDate(type))
            validator = new DateValidator(exemptDefault: metadata.MetadataKind == ModelMetadataKind.Property && Nullable.GetUnderlyingType(type) is null);
        else if (IsList(type) && !ItemsMayBeNull(metadata))
            validator = NoNullItemsValidator.Instance;
        else if (IsJsonElement(type) && context.ValidatorMetadata.OfType<RequiredAttribute>().Any())
            validator = RequiredJsonValidator.Instance;
        if (validator is not null)
            context.Results.Add(new ValidatorItem { Validator = validator, IsReusable = true });
    }

    private static Type Underlying(Type type) => Nullable.GetUnderlyingType(type) ?? type;

    private static bool IsDate(Type type) => Underlying(type) is var t && (t == typeof(DateTime) || t == typeof(DateTimeOffset) || t == typeof(DateOnly));

    private static bool IsJsonElement(Type type) => Underlying(type) == typeof(JsonElement);

    private static bool IsList(Type type) =>
        type != typeof(string) && typeof(IEnumerable).IsAssignableFrom(type) && !typeof(IDictionary).IsAssignableFrom(type) &&
        !type.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IReadOnlyDictionary<,>)) &&
        ElementType(type) is { } element && (!element.IsValueType || Nullable.GetUnderlyingType(element) is not null);

    private static Type? ElementType(Type type) =>
        type.IsArray
            ? type.GetElementType()
            : type.GetInterfaces().Append(type).FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>))?.GetGenericArguments()[0];

    /// <summary>Whether the declaration allows null items (<c>List&lt;T?&gt;</c>); unknown declarations do not.</summary>
    private static bool ItemsMayBeNull(ModelMetadata metadata)
    {
        if (ElementType(metadata.ModelType) is { IsValueType: true }) return true; // Nullable<T> items: null is a value
        try
        {
            var context = new NullabilityInfoContext();
            NullabilityInfo? info = metadata switch
            {
                { MetadataKind: ModelMetadataKind.Property, ContainerType: { } container, PropertyName: { } name } =>
                    container.GetProperty(name, BindingFlags.Public | BindingFlags.Instance) is { } property ? context.Create(property) : null,
                _ => null, // a whole-body list parameter: items must not be null
            };
            var item = info?.ElementType ?? info?.GenericTypeArguments.FirstOrDefault();
            return item?.ReadState == NullabilityState.Nullable;
        }
        catch (Exception e) when (e is AmbiguousMatchException or InvalidOperationException)
        {
            return false;
        }
    }

    private sealed class DateValidator(bool exemptDefault) : IModelValidator
    {
        public IEnumerable<ModelValidationResult> Validate(ModelValidationContext context)
        {
            int? year = context.Model switch
            {
                DateTime d when !(exemptDefault && d == default) => d.Year,
                DateTimeOffset d when !(exemptDefault && d == default) => d.Year,
                DateOnly d when !(exemptDefault && d == default) => d.Year,
                _ => null,
            };
            return year is < MinYear or > MaxYear
                ? new[] { new ModelValidationResult(string.Empty, $"Enter a date between {MinYear} and {MaxYear}.") }
                : Array.Empty<ModelValidationResult>();
        }
    }

    private sealed class NoNullItemsValidator : IModelValidator
    {
        public static readonly NoNullItemsValidator Instance = new();

        public IEnumerable<ModelValidationResult> Validate(ModelValidationContext context) =>
            context.Model is IEnumerable items && items.Cast<object?>().Any(i => i is null)
                ? new[] { new ModelValidationResult(string.Empty, "The list contains an empty (null) item.") }
                : Array.Empty<ModelValidationResult>();
    }

    private sealed class RequiredJsonValidator : IModelValidator
    {
        public static readonly RequiredJsonValidator Instance = new();

        public IEnumerable<ModelValidationResult> Validate(ModelValidationContext context) =>
            context.Model is null or JsonElement { ValueKind: JsonValueKind.Undefined or JsonValueKind.Null }
                ? new[] { new ModelValidationResult(string.Empty, $"The {context.ModelMetadata.GetDisplayName()} field is required.") }
                : Array.Empty<ModelValidationResult>();
    }
}
