using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OptimizeAll.Api.Modules.Accounts;
using OptimizeAll.Domain.Audit;
using OptimizeAll.Domain.Identity;
using OptimizeAll.IntegrationTests.Infrastructure;

namespace OptimizeAll.IntegrationTests.Accounts;

public sealed class ProfileTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private static object ValidProfile(string? whatsApp = null, bool whatsAppOptIn = false) => new
    {
        displayName = "Sara Khan",
        countryCode = "ae",
        languageCode = "AR",
        timeZone = "Asia/Dubai",
        interests = new[] { "Fashion", "tech", "fashion" },
        marketingEmailOptIn = true,
        whatsAppNumber = whatsApp,
        whatsAppOptIn,
    };

    [Fact]
    public async Task Profile_can_be_read_and_updated()
    {
        var (user, client) = await api.CreateClientAsync();
        var initial = await (await client.GetAsync("/api/v1/me/profile")).ReadJsonAsync();
        Assert.Equal(user.Email, initial.GetProperty("email").GetString());
        Assert.True(initial.GetProperty("emailVerified").GetBoolean());
        Assert.Equal("Standard", initial.GetProperty("tier").GetString());
        Assert.False(string.IsNullOrEmpty(initial.GetProperty("referralCode").GetString()));

        var updated = await (await client.PutAsJsonAsync("/api/v1/me/profile", ValidProfile("+971501234567", true))).ReadJsonAsync();
        Assert.Equal("Sara Khan", updated.GetProperty("displayName").GetString());
        Assert.Equal("AE", updated.GetProperty("countryCode").GetString());
        Assert.Equal("ar", updated.GetProperty("languageCode").GetString());
        Assert.Equal("Asia/Dubai", updated.GetProperty("timeZone").GetString());
        Assert.Equal(new[] { "fashion", "tech" }, updated.GetProperty("interests").EnumerateArray().Select(i => i.GetString()));
        Assert.Equal("+971501234567", updated.GetProperty("whatsAppNumber").GetString());
        Assert.True(updated.GetProperty("whatsAppOptIn").GetBoolean());

        // Opting out drops the stored number (data minimisation).
        var optedOut = await (await client.PutAsJsonAsync("/api/v1/me/profile", ValidProfile("+971501234567", false))).ReadJsonAsync();
        Assert.Equal(JsonValueKind.Null, optedOut.GetProperty("whatsAppNumber").ValueKind);
        Assert.True(await api.WithDbAsync(db => db.Set<AuditLog>().AnyAsync(a => a.Action == "account.profile_updated" && a.EntityId == user.Id.ToString())));
    }

    [Theory]
    [InlineData("timeZone", "Mars/Olympus", "timeZone")]
    [InlineData("whatsAppNumber", "0501234567", "whatsAppNumber")]
    [InlineData("languageCode", "english!", "languageCode")]
    public async Task Invalid_profile_fields_are_rejected_with_field_errors(string field, string value, string errorField)
    {
        var (_, client) = await api.CreateClientAsync();
        var body = JsonSerializer.SerializeToNode(ValidProfile("+971501234567", true))!.AsObject();
        body[field] = value;
        var response = await client.PutAsJsonAsync("/api/v1/me/profile", body);
        await response.ShouldFailAsync(400, "profile.invalid");
        var problem = JsonSerializer.Deserialize<JsonElement>(await response.Content.ReadAsStringAsync());
        Assert.True(problem.GetProperty("errors").TryGetProperty(errorField, out _));
    }

    [Fact]
    public async Task WhatsApp_opt_in_requires_a_number_and_interest_limits_apply()
    {
        var (_, client) = await api.CreateClientAsync();
        await (await client.PutAsJsonAsync("/api/v1/me/profile", ValidProfile(null, true))).ShouldFailAsync(400, "profile.invalid");

        var tooMany = JsonSerializer.SerializeToNode(ValidProfile())!.AsObject();
        tooMany["interests"] = JsonSerializer.SerializeToNode(Enumerable.Range(0, 21).Select(i => $"topic{i}").ToArray());
        await (await client.PutAsJsonAsync("/api/v1/me/profile", tooMany)).ShouldFailAsync(400, "profile.invalid");

        var tooLong = JsonSerializer.SerializeToNode(ValidProfile())!.AsObject();
        tooLong["interests"] = JsonSerializer.SerializeToNode(new[] { new string('a', 41) });
        await (await client.PutAsJsonAsync("/api/v1/me/profile", tooLong)).ShouldFailAsync(400, "profile.invalid");

        // DataAnnotations: display name too short, bad country.
        var bad = JsonSerializer.SerializeToNode(ValidProfile())!.AsObject();
        bad["displayName"] = "S";
        bad["countryCode"] = "ARE";
        Assert.Equal(400, (int)(await client.PutAsJsonAsync("/api/v1/me/profile", bad)).StatusCode);
    }

    [Fact]
    public async Task Payout_destination_is_encrypted_at_rest_and_only_a_masked_hint_is_returned()
    {
        var (user, client) = await api.CreateClientAsync();
        var empty = await (await client.GetAsync("/api/v1/me/payout-profile")).ReadJsonAsync();
        Assert.False(empty.GetProperty("configured").GetBoolean());

        const string iban = "GB82 WEST 1234 5698 7654 32";
        var saved = await client.PutAsJsonAsync("/api/v1/me/payout-profile", new
        {
            method = "BankTransfer", accountHolderName = "Sara Khan", destination = iban, preferredCurrency = "gbp", countryCode = "GB",
        });
        var savedText = await saved.Content.ReadAsStringAsync();
        var dto = await saved.ReadJsonAsync();
        Assert.Equal("••••5432", dto.GetProperty("destinationHint").GetString());
        Assert.Equal("GBP", dto.GetProperty("preferredCurrency").GetString());
        Assert.DoesNotContain("GB82WEST", savedText);
        Assert.DoesNotContain("12345698765432", savedText);
        Assert.False(dto.TryGetProperty("destination", out _));

        var fetched = await (await client.GetAsync("/api/v1/me/payout-profile")).Content.ReadAsStringAsync();
        Assert.DoesNotContain("GB82WEST", fetched);

        var row = await api.WithDbAsync(db => db.Set<PayoutProfile>().AsNoTracking().FirstAsync(p => p.UserId == user.Id));
        Assert.DoesNotContain("GB82WEST", row.EncryptedDestination);
        Assert.NotEqual(iban, row.EncryptedDestination);

        // The raw column value in the database (not just the EF-materialized one) never contains the raw destination.
        var rawColumn = await api.WithDbAsync(async db =>
        {
            var conn = db.Database.GetDbConnection();
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            // LOWER: SQLite stores Guids upper-case, MySQL lower-case.
            cmd.CommandText = $"SELECT EncryptedDestination FROM payout_profiles WHERE LOWER(UserId) = '{user.Id}'";
            return (string)(await cmd.ExecuteScalarAsync())!;
        });
        Assert.DoesNotContain("GB82WEST12345698765432", rawColumn);

        // The audit trail carries only the masked hint.
        var audits = await api.WithDbAsync(db => db.Set<AuditLog>().AsNoTracking()
            .Where(a => a.Action == "account.payout_profile_updated" && a.ActorUserId == user.Id).ToListAsync());
        Assert.Single(audits);
        Assert.DoesNotContain("GB82WEST", audits[0].AfterJson);

        // Finance code can reveal it through the documented internal service.
        using var scope = api.Services.CreateScope();
        var reader = scope.ServiceProvider.GetRequiredService<IPayoutDestinationReader>();
        Assert.Equal("GB82WEST12345698765432", await reader.RevealAsync(user.Id));
    }

    [Theory]
    [InlineData("PayPal", "not-an-email")]
    [InlineData("BankTransfer", "12AB")]
    [InlineData("BankTransfer", "GB00WEST12345698765432")]
    [InlineData("MobileWallet", "03001234567")]
    public async Task Payout_destination_is_validated_per_method(string method, string destination)
    {
        var (_, client) = await api.CreateClientAsync();
        var response = await client.PutAsJsonAsync("/api/v1/me/payout-profile", new
        {
            method, accountHolderName = "Sara Khan", destination, preferredCurrency = "USD",
        });
        await response.ShouldFailAsync(400, "payout_profile.invalid_destination");
    }

    [Fact]
    public async Task PayPal_and_wallet_hints_are_masked_and_currency_must_be_supported()
    {
        var (_, client) = await api.CreateClientAsync();
        var paypal = await (await client.PutAsJsonAsync("/api/v1/me/payout-profile", new
        {
            method = "PayPal", accountHolderName = "Sara Khan", destination = "Jane.Doe@Gmail.com", preferredCurrency = "USD",
        })).ReadJsonAsync();
        Assert.Equal("j•••@gmail.com", paypal.GetProperty("destinationHint").GetString());

        var wallet = await (await client.PutAsJsonAsync("/api/v1/me/payout-profile", new
        {
            method = "MobileWallet", accountHolderName = "Sara Khan", destination = "+923001234567", preferredCurrency = "PKR",
        })).ReadJsonAsync();
        Assert.Equal("••••4567", wallet.GetProperty("destinationHint").GetString());
        Assert.Equal("MobileWallet", wallet.GetProperty("method").GetString());

        await (await client.PutAsJsonAsync("/api/v1/me/payout-profile", new
        {
            method = "PayPal", accountHolderName = "Sara Khan", destination = "jane@example.com", preferredCurrency = "XYZ",
        })).ShouldFailAsync(400, "payout_profile.unsupported_currency");
    }
}
