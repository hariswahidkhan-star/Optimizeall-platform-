namespace OptimizeAll.Api.Modules.Codes;

public static class CodesModule
{
    /// <summary>Registers discount-code (affiliate) sales: programs, codes, assignments, sales, imports and reports.</summary>
    public static IServiceCollection AddCodesModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<ICodePayoutService, CodePayoutService>();
        services.AddScoped<ICodeProgramsService, CodeProgramsService>();
        services.AddScoped<IDiscountCodesService, DiscountCodesService>();
        services.AddScoped<ICodeSalesService, CodeSalesService>();
        services.AddScoped<ICodeReportsService, CodeReportsService>();
        return services;
    }
}
