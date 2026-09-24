using System.Collections;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Serialization;

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

/// <summary>
/// The API's enum JSON converter: names (as <see cref="JsonStringEnumConverter"/>) or numbers on input, names on output,
/// but an input value that is not a defined member (e.g. <c>"platform": 999</c>) is a JSON error, i.e. a 400 for that field,
/// on every request DTO whether or not it carries <see cref="DefinedEnumAttribute"/>. <c>[Flags]</c> enums accept any
/// combination of defined bits.
/// </summary>
public sealed class DefinedEnumJsonConverter : JsonConverterFactory
{
    private readonly JsonStringEnumConverter _inner = new();

    public override bool CanConvert(Type typeToConvert) => typeToConvert.IsEnum;

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options) =>
        (JsonConverter)Activator.CreateInstance(typeof(Checked<>).MakeGenericType(typeToConvert), _inner.CreateConverter(typeToConvert, options))!;

    private sealed class Checked<T>(JsonConverter inner) : JsonConverter<T> where T : struct, Enum
    {
        private readonly JsonConverter<T> _inner = (JsonConverter<T>)inner;
        private static readonly bool IsFlags = typeof(T).IsDefined(typeof(FlagsAttribute), false);
        private static readonly ulong AllFlags = Enum.GetValues<T>().Aggregate(0UL, (acc, v) => acc | Convert.ToUInt64(v));

        public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            Check(_inner.Read(ref reader, typeToConvert, options));

        public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options) => _inner.Write(writer, value, options);

        public override T ReadAsPropertyName(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            Check(_inner.ReadAsPropertyName(ref reader, typeToConvert, options));

        public override void WriteAsPropertyName(Utf8JsonWriter writer, T value, JsonSerializerOptions options) =>
            _inner.WriteAsPropertyName(writer, value, options);

        private static T Check(T value)
        {
            var defined = IsFlags ? (Convert.ToUInt64(value) & ~AllFlags) == 0 : Enum.IsDefined(value);
            return defined ? value : throw new JsonException($"'{value}' is not a valid {typeof(T).Name}.");
        }
    }
}
