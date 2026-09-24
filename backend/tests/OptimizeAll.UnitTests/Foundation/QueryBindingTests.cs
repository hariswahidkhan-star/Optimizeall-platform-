using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using OptimizeAll.Api.Common.Http;
using OptimizeAll.Api.Common.Security;

namespace OptimizeAll.UnitTests.Foundation;

/// <summary>
/// Query-string binding and paging arithmetic shared by every list endpoint.
/// </summary>
public sealed class QueryBindingTests
{
    /// <summary>
    /// A [FromQuery] object bound under a parameter name that equals one of its own properties (e.g. <c>PageQuery page</c>)
    /// makes MVC treat <c>?page=</c> as the model prefix: it then looks for <c>page.Page</c>/<c>page.PageSize</c>, so the
    /// caller's page, page size and search are silently ignored (and never validated). Every such parameter must use a
    /// name that is not one of the object's property names.
    /// </summary>
    [Fact]
    public void No_query_object_is_bound_under_the_name_of_one_of_its_own_properties()
    {
        var offenders = new List<string>();
        var assembly = typeof(HasPermissionAttribute).Assembly;
        foreach (var type in assembly.GetTypes().Where(t => t is { IsClass: true, IsAbstract: false } && typeof(ControllerBase).IsAssignableFrom(t)))
        foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
        foreach (var parameter in method.GetParameters())
        {
            if (parameter.GetCustomAttribute<FromQueryAttribute>() is not { Name: null }) continue;
            var parameterType = parameter.ParameterType;
            if (parameterType.IsPrimitive || parameterType == typeof(string) || parameterType.IsValueType || parameterType.IsArray) continue;
            if (parameterType.GetProperties().Any(p => string.Equals(p.Name, parameter.Name, StringComparison.OrdinalIgnoreCase)))
                offenders.Add($"{type.Name}.{method.Name}({parameterType.Name} {parameter.Name})");
        }
        Assert.Empty(offenders);
    }

    [Theory]
    [InlineData(1, 25, 0)]
    [InlineData(3, 25, 50)]
    [InlineData(85_899_347, 25, int.MaxValue)] // (page - 1) * pageSize = 2^31 + 46: would wrap to a negative offset
    [InlineData(int.MaxValue, 200, int.MaxValue)]
    [InlineData(10_737_419, 200, 2_147_483_600)]
    public void Skip_never_overflows(int page, int pageSize, int expected)
    {
        Assert.Equal(expected, PagingExtensions.SkipFor(page, pageSize));
        Assert.Equal(expected, new PageQuery { Page = page, PageSize = pageSize }.Skip);
    }

    private sealed record Row(Guid Id, int Rank);

    [Fact]
    public void ThenByKey_appends_a_tie_breaker_or_orders_an_unordered_query()
    {
        var a = new Row(Guid.Parse("00000000-0000-0000-0000-000000000001"), 1);
        var b = new Row(Guid.Parse("00000000-0000-0000-0000-000000000002"), 1);
        var c = new Row(Guid.Parse("00000000-0000-0000-0000-000000000003"), 0);
        var rows = new[] { b, c, a }.AsQueryable();

        Assert.Equal(new[] { c, a, b }, rows.OrderBy(r => r.Rank).ThenByKey(r => r.Id).ToArray());
        Assert.Equal(new[] { c, b, a }, rows.OrderBy(r => r.Rank).ThenByKey(r => r.Id, descending: true).ToArray());
        Assert.Equal(new[] { a, b, c }, rows.Where(r => r.Rank >= 0).ThenByKey(r => r.Id).ToArray());
    }
}
