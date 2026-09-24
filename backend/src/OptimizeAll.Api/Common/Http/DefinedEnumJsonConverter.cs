using System.Text.Json;
using System.Text.Json.Serialization;

namespace OptimizeAll.Api.Common.Http;

/// <summary>
/// The API's enum converter: enums are written as strings and read from strings or (defined) numbers, like
/// <see cref="JsonStringEnumConverter"/>, but a value that is not a member of the enum (e.g. <c>"network": 99</c> or
/// <c>"99"</c>) is a JSON error, so the request fails with 400 before any handler runs. Without it such a value was
/// stored (enums are persisted as strings) and later broke every screen and job that looks the value up.
/// <see cref="DefinedEnumAttribute"/> remains for values that do not come from JSON.
/// </summary>
public sealed class DefinedEnumJsonConverterFactory : JsonConverterFactory
{
    private readonly JsonStringEnumConverter _inner = new();

    public override bool CanConvert(Type typeToConvert) => _inner.CanConvert(typeToConvert);

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        var inner = _inner.CreateConverter(typeToConvert, options);
        return (JsonConverter)Activator.CreateInstance(typeof(DefinedEnumConverter<>).MakeGenericType(typeToConvert), inner)!;
    }

    private sealed class DefinedEnumConverter<T>(JsonConverter<T> inner) : JsonConverter<T> where T : struct, Enum
    {
        private static readonly bool IsFlags = typeof(T).IsDefined(typeof(FlagsAttribute), inherit: false);
        private static readonly ulong AllFlags = Enum.GetValues<T>().Aggregate(0UL, (acc, v) => acc | Convert.ToUInt64(v));

        public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            Check(inner.Read(ref reader, typeToConvert, options));

        public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options) => inner.Write(writer, value, options);

        public override T ReadAsPropertyName(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            Check(inner.ReadAsPropertyName(ref reader, typeToConvert, options));

        public override void WriteAsPropertyName(Utf8JsonWriter writer, T value, JsonSerializerOptions options) =>
            inner.WriteAsPropertyName(writer, value, options);

        // [Flags] enums accept any combination of defined bits; other enums only defined members.
        private static T Check(T value) => (IsFlags ? (Convert.ToUInt64(value) & ~AllFlags) == 0 : Enum.IsDefined(value))
            ? value
            : throw new JsonException($"'{value}' is not one of the values of {typeof(T).Name}.");
    }
}
