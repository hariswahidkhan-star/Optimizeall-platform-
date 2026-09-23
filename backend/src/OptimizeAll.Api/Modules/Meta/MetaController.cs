using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OptimizeAll.Api.Common.Settings;
using OptimizeAll.Domain.Common;

namespace OptimizeAll.Api.Modules.Meta;

public sealed record CurrencyDto(string Code, int MinorUnits);

public sealed record EligibilityDefaultsDto(int MinAccountAgeDays, int MinFollowers);

/// <summary>Reference data the UI needs instead of hard-coding it (currencies, platform-wide defaults).</summary>
[ApiController]
[Route("api/v1/meta")]
public sealed class MetaController(ISettingsService settings) : ControllerBase
{
    /// <summary>Currencies the platform accepts (<see cref="Money.SupportedCurrencies"/>), alphabetical.</summary>
    [AllowAnonymous]
    [HttpGet("currencies")]
    public IReadOnlyList<CurrencyDto> Currencies() =>
        Money.SupportedCurrencies.Select(Money.Normalize).Order(StringComparer.Ordinal)
            .Select(c => new CurrencyDto(c, Money.MinorUnitDigits(c))).ToList();

    /// <summary>Platform-wide social-profile eligibility minimums (admin settings) that campaigns start from.</summary>
    [Authorize]
    [HttpGet("eligibility-defaults")]
    public async Task<EligibilityDefaultsDto> EligibilityDefaults(CancellationToken ct) =>
        new(await settings.MinAccountAgeDaysAsync(ct), await settings.MinFollowersAsync(ct));
}
