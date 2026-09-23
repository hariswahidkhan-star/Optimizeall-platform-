using OptimizeAll.Domain.Common;
using OptimizeAll.Domain.Identity;

namespace OptimizeAll.Domain.Projects;

/// <summary>
/// Time spent on client work. A running timer is an entry with <see cref="StartedAt"/> set, <see cref="Minutes"/> 0 and
/// <see cref="RunningUserId"/> = the user; the unique index on RunningUserId allows one running timer per user.
/// </summary>
public class TimeEntry : AuditedEntity, IConcurrencyStamped
{
    public Guid UserId { get; set; }
    public Guid ClientAccountId { get; set; }
    public Guid ProjectId { get; set; }
    public Guid? TaskId { get; set; }

    /// <summary>Local work date the time counts towards.</summary>
    public DateOnly Date { get; set; }
    public int Minutes { get; set; }
    public bool Billable { get; set; } = true;
    public string? Note { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? EndedAt { get; set; }

    /// <summary>Only set while the timer runs (unique).</summary>
    public Guid? RunningUserId { get; set; }
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();

    public bool IsRunning => RunningUserId is not null;
}

public enum TimesheetStatus
{
    Open,
    Submitted,
    Approved,
    Rejected,
}

/// <summary>A user's week (Monday start). Entries of a submitted or approved week are locked.</summary>
public class Timesheet : AuditedEntity, IConcurrencyStamped
{
    public Guid UserId { get; set; }
    public DateOnly WeekStart { get; set; }
    public TimesheetStatus Status { get; set; } = TimesheetStatus.Open;
    public DateTime? SubmittedAt { get; set; }
    public DateTime? DecidedAt { get; set; }
    public Guid? DecidedByUserId { get; set; }
    public string? DecisionComment { get; set; }
    public int TotalMinutes { get; set; }
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();
}

/// <summary>Hourly cost/bill rate for budget burn: per user (wins) or per staff role, in a currency.</summary>
public class HourlyRate : AuditedEntity
{
    public Guid? UserId { get; set; }
    public Role? Role { get; set; }
    public decimal Rate { get; set; }
    public string Currency { get; set; } = "USD";
}

public sealed record BurnEntry(Guid UserId, int Minutes, bool Billable, IReadOnlyCollection<Role> Roles);

public sealed record RateCard(Guid? UserId, Role? Role, decimal Rate, string Currency);

public sealed record BudgetBurn(
    decimal HoursLogged, decimal BillableHours, decimal? BudgetHours, decimal? HoursBurnPercent,
    decimal AmountBurned, decimal? BudgetAmount, decimal? AmountBurnPercent, string Currency, decimal UnpricedHours);

/// <summary>Budget burn math (pure): hours from minutes; amount = billable hours × (user rate → role rate → project default).</summary>
public static class BudgetMath
{
    public static decimal Hours(int minutes) => Math.Round(minutes / 60m, 2, MidpointRounding.AwayFromZero);

    public static decimal? RateFor(BurnEntry e, IReadOnlyList<RateCard> rates, string currency, decimal? projectDefault)
    {
        var user = rates.FirstOrDefault(r => r.UserId == e.UserId && string.Equals(r.Currency, currency, StringComparison.OrdinalIgnoreCase));
        if (user is not null) return user.Rate;
        var role = rates.Where(r => r.UserId is null && r.Role is { } role && e.Roles.Contains(role) &&
                                    string.Equals(r.Currency, currency, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(r => r.Rate).FirstOrDefault();
        return role?.Rate ?? projectDefault;
    }

    public static BudgetBurn Compute(IEnumerable<BurnEntry> entries, IReadOnlyList<RateCard> rates, decimal? budgetHours,
        decimal? budgetAmount, string currency, decimal? projectDefaultRate)
    {
        var totalMinutes = 0;
        var billableMinutes = 0;
        var unpricedMinutes = 0;
        var amount = 0m;
        foreach (var e in entries)
        {
            totalMinutes += e.Minutes;
            if (!e.Billable) continue;
            billableMinutes += e.Minutes;
            var rate = RateFor(e, rates, currency, projectDefaultRate);
            if (rate is null) { unpricedMinutes += e.Minutes; continue; }
            amount += e.Minutes / 60m * rate.Value;
        }
        amount = Money.Round(amount, currency);
        var hours = Hours(totalMinutes);
        return new BudgetBurn(
            hours, Hours(billableMinutes), budgetHours,
            budgetHours is > 0 ? Math.Round(hours / budgetHours.Value * 100m, 1, MidpointRounding.AwayFromZero) : null,
            amount, budgetAmount,
            budgetAmount is > 0 ? Math.Round(amount / budgetAmount.Value * 100m, 1, MidpointRounding.AwayFromZero) : null,
            currency, Hours(unpricedMinutes));
    }

    /// <summary>Monday of the ISO week containing <paramref name="date"/>.</summary>
    public static DateOnly WeekStart(DateOnly date) => date.AddDays(-(((int)date.DayOfWeek + 6) % 7));
}
