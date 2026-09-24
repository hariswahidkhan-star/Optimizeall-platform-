using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.DependencyInjection;
using OptimizeAll.Api.Common.Security;

namespace OptimizeAll.IntegrationTests.ApiContract;

/// <summary>One bindable input of an endpoint (route value, query value, JSON body or form field/file).</summary>
public sealed record EndpointParameter(string Name, BindingSource Source, Type Type, bool IsRequired);

/// <summary>
/// An HTTP endpoint of the app as the router sees it (from <see cref="EndpointDataSource"/>), with the authorization metadata
/// that decides who may call it and the inputs MVC binds (from ApiExplorer). The expected authorization outcome is derived
/// from this metadata: <c>[HasPermission]</c> attributes must all be held, and each <c>[RequireAnyPermission]</c> needs one
/// of its permissions.
/// </summary>
public sealed class ApiEndpoint
{
    public required string Method { get; init; }
    public required string Route { get; init; }
    public required RouteEndpoint Endpoint { get; init; }
    public ControllerActionDescriptor? Action { get; init; }
    public IReadOnlyList<EndpointParameter> Parameters { get; init; } = Array.Empty<EndpointParameter>();

    public string Key => $"{Method} {Route}";
    public bool AllowAnonymous => Endpoint.Metadata.GetMetadata<IAllowAnonymous>() is not null;

    /// <summary>Permissions that must all be held (stacked <c>[HasPermission]</c>).</summary>
    public IReadOnlyList<string> AllOf => Endpoint.Metadata.OfType<IAuthorizeData>()
        .Select(a => a.Policy).Where(p => p is not null && p.StartsWith(HasPermissionAttribute.PolicyPrefix, StringComparison.Ordinal))
        .Select(p => p![HasPermissionAttribute.PolicyPrefix.Length..]).Distinct().ToList();

    /// <summary>Each set needs at least one of its permissions (<c>[RequireAnyPermission]</c>, controller and action level).</summary>
    public IReadOnlyList<IReadOnlyList<string>> AnyOf => Endpoint.Metadata.OfType<RequireAnyPermissionAttribute>()
        .Select(a => a.Permissions).ToList();

    public bool HasPermissionMetadata => AllOf.Count > 0 || AnyOf.Count > 0;

    /// <summary>Whether a caller with <paramref name="granted"/> passes the endpoint's permission metadata.</summary>
    public bool Allows(IReadOnlySet<string> granted) =>
        AllOf.All(granted.Contains) && AnyOf.All(set => set.Any(granted.Contains));

    public string Group => Action?.ControllerName ?? Route.Split('/').FirstOrDefault() ?? "misc";

    public IEnumerable<EndpointParameter> Query => Parameters.Where(p => p.Source == BindingSource.Query);
    public EndpointParameter? Body => Parameters.FirstOrDefault(p => p.Source == BindingSource.Body);
    public IEnumerable<EndpointParameter> Form => Parameters.Where(p => p.Source == BindingSource.Form || p.Source == BindingSource.FormFile);
    public bool HasBody => Body is not null;
    public bool HasForm => Form.Any();

    public IEnumerable<RoutePatternParameterPart> RouteParameters => Endpoint.RoutePattern.Parameters;

    public override string ToString() => Key;
}

public static class EndpointCatalog
{
    public static IReadOnlyList<ApiEndpoint> Load(IServiceProvider services)
    {
        var descriptions = services.GetRequiredService<IApiDescriptionGroupCollectionProvider>().ApiDescriptionGroups.Items
            .SelectMany(g => g.Items)
            .GroupBy(d => d.ActionDescriptor.Id)
            .ToDictionary(g => g.Key, g => g.First());

        var result = new List<ApiEndpoint>();
        foreach (var endpoint in services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>())
        {
            var action = endpoint.Metadata.GetMetadata<ControllerActionDescriptor>();
            var parameters = new List<EndpointParameter>();
            if (action is not null && descriptions.TryGetValue(action.Id, out var description))
            {
                foreach (var p in description.ParameterDescriptions)
                {
                    if (p.Source == BindingSource.Services || p.Source == BindingSource.Special) continue;
                    if (p.Type == typeof(CancellationToken)) continue;
                    parameters.Add(new EndpointParameter(p.Name, p.Source, p.Type ?? typeof(string), p.IsRequired));
                }
            }
            var methods = endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods ?? new[] { "*" };
            foreach (var method in methods)
            {
                result.Add(new ApiEndpoint
                {
                    Method = method, Route = endpoint.RoutePattern.RawText ?? string.Empty, Endpoint = endpoint, Action = action,
                    Parameters = parameters,
                });
            }
        }
        return result.OrderBy(e => e.Route, StringComparer.Ordinal).ThenBy(e => e.Method, StringComparer.Ordinal).ToList();
    }
}
