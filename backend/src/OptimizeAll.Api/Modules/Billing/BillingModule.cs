using Microsoft.EntityFrameworkCore;
using OptimizeAll.Api.Common.Jobs;
using OptimizeAll.Api.Common.Persistence;
using OptimizeAll.Domain.Billing;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Modules.Billing;

public static class BillingModule
{
    /// <summary>Registers the Billing module's services, jobs and event handlers.</summary>
    public static IServiceCollection AddBillingModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<BillingSettingsService>();
        services.AddScoped<DocumentNumberService>();
        services.AddSingleton<PublicLinkTokens>();
        services.AddScoped<LineBuilder>();
        services.AddScoped<InvoiceService>();
        services.AddScoped<PaymentService>();
        services.AddScoped<CreditNoteService>();
        services.AddScoped<ContractService>();
        services.AddScoped<RecurringBillingService>();
        services.AddScoped<BillingReports>();
        services.AddScoped<ClientBillingService>();
        services.AddScoped<ServiceCatalogService>();

        // Online payments: add a real adapter (Stripe, PayPal) here; the default reports "not configured".
        services.AddSingleton<IClientPaymentGateway, NotConfiguredPaymentGateway>();

        services.AddRecurringJob<RecurringInvoiceJob>(TimeSpan.FromHours(1));
        services.AddRecurringJob<InvoiceOverdueJob>(TimeSpan.FromHours(1));
        services.AddScoped<ISeeder, BillingBaselineSeeder>();
        services.AddScoped<ISeeder, SalesCatalogSeeder>();
        return services;
    }
}

/// <summary>
/// Baseline: example tax rates, all marked <see cref="TaxRate.NeedsReview"/> — tax rules depend on the agency's registration
/// and the client's location, so finance must confirm (or deactivate) each one before relying on it.
/// </summary>
public sealed class BillingBaselineSeeder : ISeeder
{
    public string Profile => "Baseline";
    public int Order => 40;

    public static readonly (string Name, decimal Rate, bool Inclusive, string? Country, string Notes)[] ExampleRates =
    {
        ("No tax", 0m, false, null, "For exempt or out-of-scope services."),
        ("UK VAT 20%", 20m, false, "GB", "Standard UK VAT rate. Confirm VAT registration and place-of-supply rules."),
        ("UAE VAT 5%", 5m, false, "AE", "Standard UAE VAT rate. Confirm registration and place-of-supply rules."),
        ("Sindh sales tax on services 13%", 13m, false, "PK", "Example provincial rate for services in Pakistan; rates differ by province."),
        ("US — no sales tax on services", 0m, false, "US", "Most US states don't tax marketing services; some do. Confirm per state."),
    };

    public async Task SeedAsync(AppDbContext db, CancellationToken ct)
    {
        if (await db.Set<TaxRate>().AnyAsync(ct)) return;
        foreach (var (name, rate, inclusive, country, notes) in ExampleRates)
            db.Set<TaxRate>().Add(new TaxRate
            {
                Name = name, RatePercent = rate, Inclusive = inclusive, CountryCode = country, IsActive = true, NeedsReview = true,
                Notes = "Example — review before use. " + notes,
            });
        await db.SaveChangesAsync(ct);
    }
}
