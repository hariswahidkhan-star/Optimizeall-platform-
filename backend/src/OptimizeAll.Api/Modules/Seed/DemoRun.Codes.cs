using Microsoft.EntityFrameworkCore;
using OptimizeAll.Api.Common.Ledger;
using OptimizeAll.Api.Modules.Codes;
using OptimizeAll.Domain.Codes;
using OptimizeAll.Domain.Common;
using OptimizeAll.Domain.Identity;
using OptimizeAll.Domain.Ledger;
using OptimizeAll.Domain.Notifications;
using OptimizeAll.Domain.Rewards;

namespace OptimizeAll.Api.Modules.Seed;

/// <summary>
/// Discount-code (affiliate) demo data (docs/DEMO.md "Discount codes"): the "Glow Cosmetics" program (10 % of net, a tier
/// after 5 sales with a 25 USD bonus, another bonus at 15, caps and a budget), codes imported from the brand, personal codes for Sara and Layla,
/// a shared code for the "Glow beauty squad" rate group (with a group payout override), and sales in every state —
/// approved (with ledger commissions carrying the payout source), pending (one matched by the brand's report), needs-info,
/// rejected and refunded (reversed). A second program is still a draft. Approvals go through the production
/// <see cref="CodePayoutService"/> and <see cref="LedgerWriter"/>, so caps, rounding and idempotency keys are real.
/// </summary>
internal sealed partial class DemoRun
{
    public const string DemoCodeProgramName = "Glow Cosmetics — summer affiliate";
    public const string DemoCodeGroupName = "Glow beauty squad";
    public const string DemoSaraCode = "GLOW-SARA15";

    private async Task CreateCodeProgramsAsync(CancellationToken ct)
    {
        var manager = Manager;
        var created = Day(-32, 10);
        _clock.Now = created;
        _audit.As(manager.Id, Role.CampaignManager);

        var program = new CodeProgram
        {
            Id = IdGenerator.NewId(created), Name = DemoCodeProgramName, BrandName = "Glow Cosmetics",
            Description = "Glow's summer skincare line. Share your personal code with your audience: they get 15% off and you earn a commission on every order.",
            Terms = "Codes are for your own audience only — no coupon sites or paid search on the brand name. Self-purchases don't count. " +
                    "Report each order within 60 days with the order number from the confirmation email. Refunded orders are reversed.",
            StoreUrl = "https://glow-cosmetics.example.com/summer", DiscountLabel = "15% off the summer collection", Currency = "USD",
            StartsAt = Day(-31, 0), EndsAt = Day(60, 0), Status = CodeProgramStatus.Active,
            PayoutType = CodePayoutType.PercentOfNet, Percent = 10m, DailyCapPerPerson = 150m, ProgramCapPerPerson = 1_000m, BudgetAmount = 5_000m,
            MaxOrderAgeDays = 60, RequireProof = false, PayoutVersion = 1, CreatedByUserId = manager.Id, CreatedAt = created, UpdatedAt = created,
        };
        program.Tiers.Add(new CodeProgramTier { ProgramId = program.Id, ThresholdSales = 5, Percent = 12m, BonusAmount = 25m });
        program.Tiers.Add(new CodeProgramTier { ProgramId = program.Id, ThresholdSales = 15, BonusAmount = 50m });
        _db.Set<CodeProgram>().Add(program);
        _audit.Record("code_program.created", nameof(CodeProgram), program.Id, after: new { program.Name, program.BrandName, program.Currency });
        Count("code programs");

        var draft = new CodeProgram
        {
            Id = IdGenerator.NewId(created), Name = "Aurora Travel — autumn codes (draft)", BrandName = "Aurora Travel",
            Description = "Being set up: codes arrive from the brand next week.", StoreUrl = "https://aurora-travel.example.com/deals",
            DiscountLabel = "10 EUR off any booking", Currency = "USD", StartsAt = Day(14, 0), Status = CodeProgramStatus.Draft,
            PayoutType = CodePayoutType.FlatPerSale, FlatAmount = 8m, CreatedByUserId = manager.Id, CreatedAt = created, UpdatedAt = created,
        };
        _db.Set<CodeProgram>().Add(draft);
        Count("code programs");
        await SaveAsync(ct);

        // Codes from the brand's CSV (one import batch) plus two added by hand.
        var batch = new CodeImportBatch
        {
            ProgramId = program.Id, Kind = CodeImportKind.Codes, FileName = "glow-summer-codes.csv", Rows = 12, Created = 12, CreatedAt = Day(-30, 11),
            CreatedByUserId = manager.Id,
        };
        _db.Set<CodeImportBatch>().Add(batch);
        var codes = new Dictionary<string, DiscountCode>();
        DiscountCode Code(string value, DiscountCodeSource source, DateTime at, DateTime? validTo = null)
        {
            var c = new DiscountCode
            {
                Id = IdGenerator.NewId(at), ProgramId = program.Id, Code = value, NormalizedCode = DiscountCode.Normalize(value), Source = source,
                ImportBatchId = source == DiscountCodeSource.Import ? batch.Id : null, ValidTo = validTo, CreatedByUserId = manager.Id, CreatedAt = at, UpdatedAt = at,
            };
            _db.Set<DiscountCode>().Add(c);
            codes[value] = c;
            Count("discount codes");
            return c;
        }
        foreach (var value in new[] { DemoSaraCode, "GLOW-LAYLA15", "GLOWSQUAD", "GLOW-7KQ2", "GLOW-8MZ4", "GLOW-9TR6", "GLOW-3HX8", "GLOW-5WD3",
                     "GLOW-6PL9", "GLOW-2VN7", "GLOW-4CJ5", "GLOW-EXPIRED" })
            Code(value, DiscountCodeSource.Import, Day(-30, 11), value == "GLOW-EXPIRED" ? Day(-5, 0) : null);
        _audit.Record("discount_code.imported", nameof(CodeProgram), program.Id, after: new { BatchId = batch.Id, Created = 12, File = batch.FileName });
        Code("GLOW-HANNAH", DiscountCodeSource.Manual, Day(-8, 9));
        Code("GLOW-PAUSED", DiscountCodeSource.Manual, Day(-8, 9)).Status = DiscountCodeStatus.Paused;
        foreach (var assigned in new[] { DemoSaraCode, "GLOW-LAYLA15", "GLOWSQUAD", "GLOW-HANNAH" })
            codes[assigned].Status = DiscountCodeStatus.Assigned;
        codes["GLOW-4CJ5"].Status = DiscountCodeStatus.Retired;
        codes["GLOW-4CJ5"].Note = "Leaked on a coupon site";
        await SaveAsync(ct);

        // The demo group (a manual rate group) that shares one code.
        var sara = Sara;
        var layla = Person("layla.haddad");
        var squad = new[] { Person("zainab.malik"), Person("kavya.iyer"), Person("mona.farouk"), Person("chloe.evans") };
        var group = new RateGroup
        {
            Id = IdGenerator.NewId(Day(-29, 9)), Name = DemoCodeGroupName, Description = "Beauty creators sharing the GLOWSQUAD code.", Priority = 5,
            MembershipMode = RateGroupMembershipMode.Manual, CreatedByUserId = manager.Id, CreatedAt = Day(-29, 9), UpdatedAt = Day(-29, 9),
        };
        _db.Set<RateGroup>().Add(group);
        foreach (var p in squad)
        {
            _db.Set<RateGroupMember>().Add(new RateGroupMember { GroupId = group.Id, UserId = p.Id, AddedAt = Day(-29, 9), AddedByUserId = manager.Id, Note = "Glow launch" });
            _db.Set<RateGroupMemberEvent>().Add(new RateGroupMemberEvent
            {
                GroupId = group.Id, UserId = p.Id, Action = RateGroupMemberAction.Added, At = Day(-29, 9), ActorUserId = manager.Id, Source = "bulk", Reason = "Glow launch",
            });
        }
        Count("rate groups");

        DiscountCodeAssignment Assign(DiscountCode code, DemoPerson? person, RateGroup? toGroup, DateTime from, string reason)
        {
            var a = new DiscountCodeAssignment
            {
                Id = IdGenerator.NewId(from), CodeId = code.Id, ProgramId = program.Id, Target = person is null ? CodeAssignmentTarget.Group : CodeAssignmentTarget.Person,
                UserId = person?.Id, GroupId = toGroup?.Id, ValidFrom = from, Reason = reason, CreatedAt = from, CreatedByUserId = manager.Id,
            };
            _db.Set<DiscountCodeAssignment>().Add(a);
            _clock.Now = from;
            _audit.Record("discount_code.assigned", nameof(DiscountCode), code.Id,
                after: new { Target = a.Target.ToString(), a.UserId, a.GroupId, ValidFrom = from }, reason: reason);
            Count("code assignments");
            return a;
        }
        var saraCode = Assign(codes[DemoSaraCode], sara, null, Day(-28, 10), "Top beauty creator");
        var laylaCode = Assign(codes["GLOW-LAYLA15"], layla, null, Day(-28, 10), "Negotiated flat deal");
        var squadCode = Assign(codes["GLOWSQUAD"], null, group, Day(-27, 10), "Shared squad code");
        Assign(codes["GLOW-HANNAH"], Person("hannah.lee"), null, Day(-8, 10), "Platinum creator");
        await SaveAsync(ct);
        foreach (var (p, code) in new[] { (sara, DemoSaraCode), (layla, "GLOW-LAYLA15") })
            await NotifyAsync(p.Id, NotificationTypes.CodeAssigned, "New discount code",
                $"Glow Cosmetics code {code} is now yours. Share it and report the sales made with it.", Common.Notifications.AppLinks.MyCodes);

        // Payout overrides: Layla's negotiated flat fee; 12 % for the squad.
        _db.Set<CodePayoutOverride>().Add(new CodePayoutOverride
        {
            ProgramId = program.Id, Target = CodeAssignmentTarget.Person, UserId = layla.Id, PayoutType = CodePayoutType.FlatPerSale, FlatAmount = 6m,
            Reason = "Negotiated: 6 USD per order", CreatedAt = Day(-28, 11), CreatedByUserId = manager.Id,
        });
        _db.Set<CodePayoutOverride>().Add(new CodePayoutOverride
        {
            ProgramId = program.Id, Target = CodeAssignmentTarget.Group, GroupId = group.Id, PayoutType = CodePayoutType.PercentOfNet, Percent = 12m,
            Reason = "Squad launch rate", CreatedAt = Day(-27, 11), CreatedByUserId = manager.Id,
        });
        await SaveAsync(ct);

        // Sales, oldest first; approvals priced by the production calculator.
        var payouts = new CodePayoutService(_db);
        async Task<CodeSale> Sale(DemoPerson person, DiscountCodeAssignment a, string reference, int daysAgo, decimal net, decimal discount,
            string note, CodeSaleSource source = CodeSaleSource.Participant, DemoPerson? createdBy = null)
        {
            var at = Day(-daysAgo, 12);
            _clock.Now = at.AddHours(3);
            var s = new CodeSale
            {
                Id = IdGenerator.NewId(_clock.Now), ProgramId = program.Id, CodeId = a.CodeId, UserId = person.Id, AssignmentId = a.Id, GroupId = a.GroupId,
                OrderReference = reference, NormalizedOrderReference = CodeSale.NormalizeOrderReference(reference),
                ActiveOrderKey = CodeSale.NormalizeOrderReference(reference), OrderDate = at, NetAmount = net, DiscountAmount = discount, Currency = "USD",
                ExchangeRate = 1m, ProgramNetAmount = net, ProgramDiscountAmount = discount, ProductNote = note, Status = CodeSaleStatus.Pending,
                Source = source, CreatedByUserId = (createdBy ?? person).Id, SubmittedAt = _clock.Now, CreatedAt = _clock.Now, UpdatedAt = _clock.Now,
            };
            var loaded = await LoadDemoProgramAsync(program.Id, ct);
            s.EstimatedCommission = await payouts.EstimateAsync(loaded, person.Id, net, null, ct);
            _db.Set<CodeSale>().Add(s);
            _db.Set<CodeSaleEvent>().Add(new CodeSaleEvent
            {
                SaleId = s.Id, ToStatus = CodeSaleStatus.Pending, Action = source == CodeSaleSource.Participant ? "submitted" : "entered",
                ActorUserId = s.CreatedByUserId, At = _clock.Now,
            });
            _audit.As(s.CreatedByUserId, source == CodeSaleSource.Participant ? Role.Participant : Role.CampaignManager)
                .Record("code_sale.submitted", nameof(CodeSale), s.Id, after: new { s.OrderReference, s.NetAmount, s.Currency });
            Count("code sales");
            await SaveAsync(ct);
            return s;
        }

        async Task Decide(CodeSale s, DemoPerson reviewer, CodeSaleStatus to, int hoursLater, string? reason = null)
        {
            _clock.Now = s.SubmittedAt.AddHours(hoursLater);
            var sale = _db.Set<CodeSale>().First(x => x.Id == s.Id);
            var from = sale.Status;
            _audit.As(reviewer.Id, Role.Reviewer);
            if (to == CodeSaleStatus.Approved)
            {
                var loaded = await LoadDemoProgramAsync(program.Id, ct);
                var priced = await payouts.PriceAsync(loaded, sale, ct);
                var source = priced.RateSource(loaded);
                foreach (var line in priced.Result.Lines)
                {
                    var commission = line.Kind == CodePayoutLineKind.Commission;
                    await _ledger.RecordAsync(new NewEarning(sale.UserId, commission ? EarningType.SaleCommission : EarningType.SaleTierBonus, line.Amount,
                        loaded.Currency, commission ? CodePayoutService.CommissionKey(sale.Id) : CodePayoutService.TierBonusKey(loaded.Id, sale.UserId, line.TierThreshold!.Value),
                        commission ? $"Sale commission — {loaded.BrandName} order {sale.OrderReference}" : $"{line.Label} — {loaded.BrandName}",
                        RequiresApproval: false, CreatedByUserId: reviewer.Id,
                        RateSource: commission ? source : source with { Label = $"{loaded.Name} v{priced.PayoutVersion} · {line.Label}" },
                        CodeProgramId: loaded.Id, CodeSaleId: sale.Id));
                    Count("ledger entries");
                }
                sale.CommissionAmount = priced.Result.Total;
                sale.PayoutSourceLabel = source.Label.Length > 200 ? source.Label[..200] : source.Label;
                sale.AppliedCaps = priced.Result.AppliedCaps.Count > 0 ? string.Join(",", priced.Result.AppliedCaps) : null;
                sale.PayoutVersion = priced.PayoutVersion;
                await NotifyAsync(sale.UserId, NotificationTypes.CodeSaleDecision, "Sale approved",
                    $"Your Glow Cosmetics sale {sale.OrderReference} was approved. You earned {Money2(priced.Result.Total, loaded.Currency)}.",
                    Common.Notifications.AppLinks.MyCodeSale(sale.Id));
            }
            else if (to == CodeSaleStatus.Rejected)
            {
                sale.ActiveOrderKey = null;
            }
            sale.Status = to;
            sale.DecidedAt = _clock.Now;
            sale.DecidedByUserId = reviewer.Id;
            sale.DecisionReason = reason;
            _db.Set<CodeSaleEvent>().Add(new CodeSaleEvent
            {
                SaleId = sale.Id, FromStatus = from, ToStatus = to,
                Action = to switch { CodeSaleStatus.Approved => "approved", CodeSaleStatus.Rejected => "rejected", _ => "info_requested" },
                ActorUserId = reviewer.Id, Reason = reason, At = _clock.Now,
            });
            _audit.Record($"code_sale.{(to == CodeSaleStatus.NeedsInfo ? "info_requested" : to.ToString().ToLowerInvariant())}", nameof(CodeSale), sale.Id,
                new { Status = from.ToString() }, new { Status = to.ToString(), sale.CommissionAmount }, reason);
            await SaveAsync(ct);
        }

        var r1 = Reviewer1;
        var r2 = Reviewer2;
        // Sara: approved sales (the 6th is priced at the 12 % tier), one pending, one needs info, one rejected, one refunded.
        var saraSales = new List<CodeSale>();
        var saraOrders = new (int Days, decimal Net, string Note)[]
        {
            (26, 84.00m, "Summer glow set"), (23, 42.50m, "Vitamin C serum"), (20, 129.99m, "Skincare bundle"), (17, 58.00m, "Sunscreen duo"),
            (14, 96.40m, "Glow set + mask"), (11, 73.25m, "Night cream"),
        };
        var n = 0;
        foreach (var (days, net, note) in saraOrders)
        {
            var s = await Sale(sara, saraCode, $"GC-{100231 + n++}", days, net, Money.Round(net * 0.15m / 0.85m, "USD"), note);
            await Decide(s, n % 2 == 0 ? r2 : r1, CodeSaleStatus.Approved, 20);
            saraSales.Add(s);
        }
        var refundedSale = saraSales[1];
        await Sale(sara, saraCode, "GC-100310", 2, 64.00m, 11.29m, "Lip oil trio");
        var saraInfo = await Sale(sara, saraCode, "GC-100287", 6, 150.00m, 26.47m, "Gift box for a friend");
        await Decide(saraInfo, r1, CodeSaleStatus.NeedsInfo, 18, "Please attach the order confirmation — the brand can't find this order number.");
        var saraRejected = await Sale(sara, saraCode, "GC-099870", 9, 35.00m, 6.18m, "Face wash");
        await Decide(saraRejected, r2, CodeSaleStatus.Rejected, 26, "This order was placed with another creator's code according to the brand.");

        // Layla (6 USD flat per order) and the squad (shared code, 12 % group rate).
        var laylaSale = await Sale(layla, laylaCode, "GC-100245", 19, 212.00m, 37.41m, "Full routine");
        await Decide(laylaSale, r1, CodeSaleStatus.Approved, 30);
        await Sale(layla, laylaCode, "GC-100318", 1, 88.00m, 15.53m, "Serum refill");
        var zainabSale = await Sale(squad[0], squadCode, "GC-100251", 16, 67.00m, 11.82m, "Glow set");
        await Decide(zainabSale, r2, CodeSaleStatus.Approved, 22);
        var kavyaMatched = await Sale(squad[1], squadCode, "GC-100299", 4, 45.00m, 7.94m, "Clay mask");
        await Sale(squad[2], squadCode, "GC-100305", 3, 120.00m, 21.18m, "Travel kit");

        // The brand's weekly sales report: matched Kavya's order, reported a refund of one of Sara's and an order nobody claimed.
        _clock.Now = Day(-1, 9);
        _audit.As(manager.Id, Role.CampaignManager);
        var report = new CodeImportBatch
        {
            ProgramId = program.Id, Kind = CodeImportKind.Sales, FileName = "glow-weekly-report.csv", Rows = 4, Created = 1, Matched = 1, Flagged = 0,
            CreatedAt = _clock.Now, CreatedByUserId = manager.Id,
        };
        _db.Set<CodeImportBatch>().Add(report);
        var matched = _db.Set<CodeSale>().First(x => x.Id == kavyaMatched.Id);
        matched.Verification = CodeSaleVerification.Matched;
        matched.VerificationNote = "Matches the reported sale.";
        matched.ReportedNetAmount = matched.NetAmount;
        matched.ReportedOrderDate = matched.OrderDate;
        matched.ImportBatchId = report.Id;
        var unclaimed = new CodeSale
        {
            Id = IdGenerator.NewId(_clock.Now), ProgramId = program.Id, CodeId = saraCode.CodeId, UserId = sara.Id, AssignmentId = saraCode.Id,
            OrderReference = "GC-100302", NormalizedOrderReference = "GC-100302", ActiveOrderKey = "GC-100302", OrderDate = Day(-3, 16), NetAmount = 55.00m,
            DiscountAmount = 9.71m, Currency = "USD", ExchangeRate = 1m, ProgramNetAmount = 55.00m, ProgramDiscountAmount = 9.71m, Status = CodeSaleStatus.Pending,
            Source = CodeSaleSource.Import, CreatedByUserId = manager.Id, SubmittedAt = _clock.Now, Verification = CodeSaleVerification.ReportedByBrand,
            VerificationNote = "Created from the brand's sales report (not claimed by the participant).", ReportedNetAmount = 55.00m,
            ReportedOrderDate = Day(-3, 16), ImportBatchId = report.Id, EstimatedCommission = 6.60m, CreatedAt = _clock.Now, UpdatedAt = _clock.Now,
        };
        _db.Set<CodeSale>().Add(unclaimed);
        _db.Set<CodeSaleEvent>().Add(new CodeSaleEvent
        {
            SaleId = unclaimed.Id, ToStatus = CodeSaleStatus.Pending, Action = "imported", ActorUserId = manager.Id,
            Reason = "Unclaimed use from the brand's sales report", At = _clock.Now,
        });
        Count("code sales");
        // Refund reported for one of Sara's approved orders: its commission is reversed (not yet paid → cancelled).
        var refund = _db.Set<CodeSale>().First(x => x.Id == refundedSale.Id);
        var earning = _db.Set<EarningEntry>().First(e => e.CodeSaleId == refund.Id && e.Type == EarningType.SaleCommission);
        await _ledger.ReverseAsync(earning, "Order refunded: brand report", manager.Id);
        Count("ledger entries");
        refund.Status = CodeSaleStatus.Refunded;
        refund.RefundedAt = _clock.Now;
        refund.RefundReason = "Brand report: order refunded";
        refund.ImportBatchId = report.Id;
        _db.Set<CodeSaleEvent>().Add(new CodeSaleEvent
        {
            SaleId = refund.Id, FromStatus = CodeSaleStatus.Approved, ToStatus = CodeSaleStatus.Refunded, Action = "refunded_by_report",
            ActorUserId = manager.Id, Reason = "Brand report: order refunded", At = _clock.Now,
        });
        _audit.Record("code_sale.report_imported", nameof(CodeProgram), program.Id, after: new { BatchId = report.Id, Matched = 1, Created = 1, Refunded = 1 });
        await NotifyAsync(sara.Id, NotificationTypes.CodeSaleDecision, "Sale refunded",
            $"The Glow Cosmetics order {refund.OrderReference} was refunded, so its commission was reversed.", Common.Notifications.AppLinks.MyCodeSale(refund.Id));
        await SaveAsync(ct);
        _clock.Now = _now;
    }

    private Task<CodeProgram> LoadDemoProgramAsync(Guid id, CancellationToken ct) =>
        _db.Set<CodeProgram>().AsNoTracking().Include(p => p.Tiers).FirstAsync(p => p.Id == id, ct);
}
