using System.Linq.Expressions;
using System.Reflection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using Microsoft.EntityFrameworkCore.Storage;

namespace OptimizeAll.Api.Common.Persistence;

/// <summary>
/// Closes the gaps between EF Core 8's SQLite and MySQL query translation that this codebase relies on, so LINQ stays
/// portable:
/// <list type="bullet">
/// <item><b>Exact decimal aggregates.</b> SQLite has no decimal type: EF Core 8 stores <c>decimal</c> as TEXT and
/// translates decimal arithmetic and comparisons with its own <c>ef_add</c>/<c>ef_compare</c>/… functions (computed in
/// .NET <c>decimal</c>), but refuses <c>Sum</c>/<c>Average</c>/<c>Min</c>/<c>Max</c> over decimals. This adds
/// aggregate functions <c>ef_sum</c>, <c>ef_avg</c>, <c>ef_min</c>, <c>ef_max</c> implemented in .NET <c>decimal</c> (no
/// floating point, so money totals are exact) and maps the LINQ aggregates of decimal expressions to them.
/// (EF Core 9 ships the equivalent.)</item>
/// <item><b><c>Guid.NewGuid()</c> in SQL</b> (e.g. rotating concurrency stamps inside <c>ExecuteUpdate</c>), which Pomelo
/// translates to <c>UUID()</c>: mapped to <c>ef_newguid()</c>, a new upper-case GUID per row, the format EF uses for
/// Guids on SQLite.</item>
/// <item><b>Ordering by decimals</b>, which EF Core 8 refuses on SQLite: decimal ORDER BY keys are rewritten to
/// <c>CAST(x AS TEXT) COLLATE EF_DECIMAL</c>, a collation that compares the values exactly as .NET decimals.</item>
/// </list>
/// The functions are registered on every SQLite connection EF creates.
/// </summary>
public static class SqliteQuerySupport
{
    public static DbContextOptionsBuilder UseSqliteQuerySupport(this DbContextOptionsBuilder options)
    {
        ((IDbContextOptionsBuilderInfrastructure)options).AddOrUpdateExtension(new Extension());
        options.AddInterceptors(FunctionRegistrar.Instance);
        return options;
    }

    /// <summary>Registers the functions on a connection (kept by the SqliteConnection across open/close).</summary>
    public static void Register(SqliteConnection connection)
    {
        connection.CreateAggregate<decimal?, decimal?, decimal?>("ef_sum", null,
            (acc, value) => value is null ? acc : (acc ?? 0m) + value.Value, acc => acc, isDeterministic: true);
        connection.CreateAggregate<decimal?, decimal?, decimal?>("ef_min", null,
            (acc, value) => value is null ? acc : acc is null || value.Value < acc.Value ? value : acc, acc => acc, isDeterministic: true);
        connection.CreateAggregate<decimal?, decimal?, decimal?>("ef_max", null,
            (acc, value) => value is null ? acc : acc is null || value.Value > acc.Value ? value : acc, acc => acc, isDeterministic: true);
        connection.CreateAggregate<decimal?, (decimal Sum, long Count), decimal?>("ef_avg", (0m, 0L),
            (acc, value) => value is null ? acc : (acc.Sum + value.Value, acc.Count + 1),
            acc => acc.Count == 0 ? null : acc.Sum / acc.Count, isDeterministic: true);
        connection.CreateFunction("ef_newguid", () => Guid.NewGuid().ToString().ToUpperInvariant(), isDeterministic: false);
        connection.CreateCollation(DecimalCollation, CompareDecimalText);
    }

    /// <summary>Collation that orders decimal TEXT values numerically (exact, in .NET decimal).</summary>
    public const string DecimalCollation = "EF_DECIMAL";

    private static int CompareDecimalText(string? x, string? y)
    {
        var a = ParseDecimal(x);
        var b = ParseDecimal(y);
        if (a is null || b is null) return string.CompareOrdinal(x, y);
        return a.Value.CompareTo(b.Value);
    }

    private static decimal? ParseDecimal(string? text) =>
        decimal.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var value)
            ? value
            : null;

    /// <summary>Marker for a decimal ORDER BY key; translated to <c>CAST(x AS TEXT) COLLATE EF_DECIMAL</c>.</summary>
    public static string DecimalOrderKey(decimal? value) =>
        throw new InvalidOperationException("Only for use in SQLite LINQ-to-Entities ordering.");

    private sealed class FunctionRegistrar : DbConnectionInterceptor
    {
        public static readonly FunctionRegistrar Instance = new();

        public override System.Data.Common.DbConnection ConnectionCreated(ConnectionCreatedEventData eventData, System.Data.Common.DbConnection result)
        {
            if (result is SqliteConnection sqlite) Register(sqlite);
            return result;
        }
    }

    private sealed class Extension : IDbContextOptionsExtension
    {
        private DbContextOptionsExtensionInfo? _info;

        public DbContextOptionsExtensionInfo Info => _info ??= new ExtensionInfo(this);

        public void ApplyServices(IServiceCollection services)
        {
            new EntityFrameworkRelationalServicesBuilder(services)
                .TryAdd<IAggregateMethodCallTranslatorPlugin, AggregatePlugin>()
                .TryAdd<IMethodCallTranslatorPlugin, MethodPlugin>();
            // Overrides the SQLite provider's factory: the last registration wins. (Not Replace: EF's service map is
            // index-based, so removing a descriptor would corrupt it.)
            services.AddScoped<IQueryableMethodTranslatingExpressionVisitorFactory, OrderingVisitorFactory>();
        }

        public void Validate(IDbContextOptions options)
        {
        }

        private sealed class ExtensionInfo(IDbContextOptionsExtension extension) : DbContextOptionsExtensionInfo(extension)
        {
            public override bool IsDatabaseProvider => false;
            public override string LogFragment => "using SqliteQuerySupport ";
            public override int GetServiceProviderHashCode() => 0;
            public override bool ShouldUseSameServiceProvider(DbContextOptionsExtensionInfo other) => other is ExtensionInfo;
            public override void PopulateDebugInfo(IDictionary<string, string> debugInfo) => debugInfo["SqliteQuerySupport"] = "1";
        }
    }

    private sealed class AggregatePlugin(ISqlExpressionFactory sql) : IAggregateMethodCallTranslatorPlugin
    {
        public IEnumerable<IAggregateMethodCallTranslator> Translators { get; } = new[] { new DecimalAggregateTranslator(sql) };
    }

    private sealed class MethodPlugin(ISqlExpressionFactory sql, IRelationalTypeMappingSource typeMappings) : IMethodCallTranslatorPlugin
    {
        public IEnumerable<IMethodCallTranslator> Translators { get; } = new IMethodCallTranslator[]
        {
            new NewGuidTranslator(sql, typeMappings),
            new DecimalOrderKeyTranslator(sql, typeMappings),
        };
    }

    private sealed class DecimalAggregateTranslator(ISqlExpressionFactory sql) : IAggregateMethodCallTranslator
    {
        public SqlExpression? Translate(MethodInfo method, EnumerableExpression source, IReadOnlyList<SqlExpression> arguments,
            IDiagnosticsLogger<DbLoggerCategory.Query> logger)
        {
            if (method.DeclaringType != typeof(Queryable) && method.DeclaringType != typeof(Enumerable)) return null;
            var function = method.Name switch
            {
                nameof(Queryable.Sum) => "ef_sum",
                nameof(Queryable.Average) => "ef_avg",
                nameof(Queryable.Min) => "ef_min",
                nameof(Queryable.Max) => "ef_max",
                _ => null,
            };
            if (function is null || source.Selector is not SqlExpression selector) return null;
            if ((Nullable.GetUnderlyingType(selector.Type) ?? selector.Type) != typeof(decimal)) return null;

            // Same shaping as EF's relational aggregate translator: a filtered aggregate becomes CASE WHEN, DISTINCT wraps.
            SqlExpression argument = selector;
            if (source.Predicate is not null)
                argument = sql.Case(new[] { new CaseWhenClause(source.Predicate, argument) }, elseResult: null);
            if (source.IsDistinct) argument = new DistinctExpression(argument);

            var aggregate = sql.Function(function, new[] { argument }, nullable: true, argumentsPropagateNullability: new[] { false },
                selector.Type, selector.TypeMapping);
            // LINQ Sum of an empty set is 0 (not null), as EF's own SUM translation does with COALESCE.
            return function == "ef_sum"
                ? sql.Coalesce(aggregate, sql.Constant(0m, selector.TypeMapping), selector.TypeMapping)
                : aggregate;
        }
    }

    private sealed class DecimalOrderKeyTranslator(ISqlExpressionFactory sql, IRelationalTypeMappingSource typeMappings) : IMethodCallTranslator
    {
        private static readonly MethodInfo OrderKey = typeof(SqliteQuerySupport).GetMethod(nameof(DecimalOrderKey))!;

        public SqlExpression? Translate(SqlExpression? instance, MethodInfo method, IReadOnlyList<SqlExpression> arguments,
            IDiagnosticsLogger<DbLoggerCategory.Query> logger)
        {
            if (!method.Equals(OrderKey)) return null;
            var text = typeMappings.FindMapping(typeof(string));
            return new CollateExpression(sql.Convert(arguments[0], typeof(string), text), DecimalCollation);
        }
    }

#pragma warning disable EF1001 // Extends the SQLite provider's (pubternal) queryable translator to lift its decimal ORDER BY restriction.
    private sealed class OrderingVisitorFactory(
        QueryableMethodTranslatingExpressionVisitorDependencies dependencies,
        RelationalQueryableMethodTranslatingExpressionVisitorDependencies relationalDependencies)
        : IQueryableMethodTranslatingExpressionVisitorFactory
    {
        public QueryableMethodTranslatingExpressionVisitor Create(QueryCompilationContext queryCompilationContext) =>
            new OrderingVisitor(dependencies, relationalDependencies, queryCompilationContext);
    }

    /// <summary>
    /// Rewrites decimal ordering keys to <see cref="DecimalOrderKey"/> (TEXT with the exact EF_DECIMAL collation), which
    /// the SQLite provider accepts in ORDER BY.
    /// </summary>
    private sealed class OrderingVisitor : Microsoft.EntityFrameworkCore.Sqlite.Query.Internal.SqliteQueryableMethodTranslatingExpressionVisitor
    {
        private static readonly MethodInfo OrderKey = typeof(SqliteQuerySupport).GetMethod(nameof(DecimalOrderKey))!;

        public OrderingVisitor(
            QueryableMethodTranslatingExpressionVisitorDependencies dependencies,
            RelationalQueryableMethodTranslatingExpressionVisitorDependencies relationalDependencies,
            QueryCompilationContext queryCompilationContext)
            : base(dependencies, relationalDependencies, queryCompilationContext)
        {
        }

        private OrderingVisitor(OrderingVisitor parent) : base(parent)
        {
        }

        protected override QueryableMethodTranslatingExpressionVisitor CreateSubqueryVisitor() => new OrderingVisitor(this);

        protected override ShapedQueryExpression? TranslateOrderBy(ShapedQueryExpression source, LambdaExpression keySelector, bool ascending) =>
            base.TranslateOrderBy(source, Rewrite(keySelector), ascending);

        protected override ShapedQueryExpression? TranslateThenBy(ShapedQueryExpression source, LambdaExpression keySelector, bool ascending) =>
            base.TranslateThenBy(source, Rewrite(keySelector), ascending);

        private static LambdaExpression Rewrite(LambdaExpression keySelector)
        {
            var type = Nullable.GetUnderlyingType(keySelector.Body.Type) ?? keySelector.Body.Type;
            if (type != typeof(decimal)) return keySelector;
            var body = Expression.Call(OrderKey,
                Expression.Convert(keySelector.Body, typeof(decimal?)));
            return Expression.Lambda(body, keySelector.Parameters);
        }
    }
#pragma warning restore EF1001

    private sealed class NewGuidTranslator(ISqlExpressionFactory sql, IRelationalTypeMappingSource typeMappings) : IMethodCallTranslator
    {
        private static readonly MethodInfo NewGuid = typeof(Guid).GetMethod(nameof(Guid.NewGuid), Type.EmptyTypes)!;

        public SqlExpression? Translate(SqlExpression? instance, MethodInfo method, IReadOnlyList<SqlExpression> arguments,
            IDiagnosticsLogger<DbLoggerCategory.Query> logger) =>
            method.Equals(NewGuid)
                ? sql.Function("ef_newguid", Array.Empty<SqlExpression>(), nullable: false, argumentsPropagateNullability: Array.Empty<bool>(),
                    typeof(Guid), typeMappings.FindMapping(typeof(Guid)))
                : null;
    }
}
