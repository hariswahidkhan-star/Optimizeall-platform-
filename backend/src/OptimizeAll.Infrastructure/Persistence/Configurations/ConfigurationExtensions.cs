using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace OptimizeAll.Infrastructure.Persistence.Configurations;

internal static class ConfigurationExtensions
{
    /// <summary>Maps a List&lt;T&gt; property to a MySQL JSON column.</summary>
    public static PropertyBuilder<List<T>> HasJsonList<T>(this PropertyBuilder<List<T>> builder) =>
        builder.HasConversion(JsonColumn.ListConverter<T>(), JsonColumn.ListComparer<T>())
            .HasColumnType("json")
            .IsRequired();
}
