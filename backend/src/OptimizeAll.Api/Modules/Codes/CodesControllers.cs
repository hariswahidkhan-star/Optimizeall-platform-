using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using OptimizeAll.Api.Common.Http;
using OptimizeAll.Api.Common.Security;
using OptimizeAll.Domain.Common;

namespace OptimizeAll.Api.Modules.Codes;

// Discount-code (affiliate) sales, docs/DISCOUNT_CODES.md and docs/api/discount-codes.md. Every staff write moves money or
// decides who may claim it (payout rules, codes, assignments, imports, approvals, refunds), so all of them are denied while
// impersonating; reads are allowed (the impersonation audit records them).

/// <summary>Programs (brand, window, terms, payout rules and overrides), their codes, assignments, staff-entered sales and imports.</summary>
[DeniedWhileImpersonating(WritesOnly = true)]
[ApiController]
[Route("api/v1/admin/code-programs")]
public sealed class CodeProgramsController(ICodeProgramsService programs, IDiscountCodesService codes, ICodeSalesService sales) : ControllerBase
{
    [HttpGet]
    [HasPermission(Permissions.CodesView)]
    public Task<PagedResult<CodeProgramListItemDto>> List([FromQuery] CodeProgramQuery query, CancellationToken ct) => programs.ListAsync(query, ct);

    [HttpGet("{id:guid}")]
    [HasPermission(Permissions.CodesView)]
    public Task<CodeProgramDto> Get(Guid id, CancellationToken ct) => programs.GetAsync(id, ct);

    /// <summary>Creates a program with its payout rules (draft unless "activate": true). 409 fx.rate_missing when commissions couldn't be settled.</summary>
    [HttpPost]
    [HasPermission(Permissions.CodesManage)]
    public async Task<ActionResult<CodeProgramDto>> Create(CreateCodeProgramRequest request, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, await programs.CreateAsync(request, ct));

    /// <summary>Edits brand, window, terms and store URL (payout rules change through PUT payout). 409 concurrency.conflict / code_program.currency_locked.</summary>
    [HttpPut("{id:guid}")]
    [HasPermission(Permissions.CodesManage)]
    public Task<CodeProgramDto> Update(Guid id, UpdateCodeProgramRequest request, CancellationToken ct) => programs.UpdateAsync(id, request, ct);

    /// <summary>Replaces the payout rules (confirm + reason; audited; bumps payoutVersion). Applies to sales approved from now on.</summary>
    [HttpPut("{id:guid}/payout")]
    [HasPermission(Permissions.CodesManage)]
    public Task<CodeProgramDto> UpdatePayout(Guid id, UpdatePayoutRulesRequest request, CancellationToken ct) => programs.UpdatePayoutAsync(id, request, ct);

    /// <summary>Active / Paused / Archived (archiving needs every pending sale decided: 409 code_program.has_pending_sales).</summary>
    [HttpPost("{id:guid}/status")]
    [HasPermission(Permissions.CodesManage)]
    public Task<CodeProgramDto> ChangeStatus(Guid id, ChangeProgramStatusRequest request, CancellationToken ct) => programs.ChangeStatusAsync(id, request, ct);

    /// <summary>Per-person or per-rate-group payout override (replaces the per-sale rate). 409 code_override.duplicate.</summary>
    [HttpPost("{id:guid}/overrides")]
    [HasPermission(Permissions.CodesManage)]
    public async Task<ActionResult<CodeProgramDto>> CreateOverride(Guid id, CreatePayoutOverrideRequest request, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, await programs.CreateOverrideAsync(id, request, ct));

    [HttpPost("{id:guid}/overrides/{overrideId:guid}/end")]
    [HasPermission(Permissions.CodesManage)]
    public Task<CodeProgramDto> EndOverride(Guid id, Guid overrideId, EndPayoutOverrideRequest request, CancellationToken ct) =>
        programs.EndOverrideAsync(id, overrideId, request, ct);

    // ---- codes

    [HttpGet("{id:guid}/codes")]
    [HasPermission(Permissions.CodesView)]
    public Task<PagedResult<DiscountCodeDto>> Codes(Guid id, [FromQuery] DiscountCodeQuery query, CancellationToken ct) => codes.ListAsync(id, query, ct);

    /// <summary>Adds one code. 409 code.duplicate (codes are unique per program, case-insensitive).</summary>
    [HttpPost("{id:guid}/codes")]
    [HasPermission(Permissions.CodesManage)]
    public async Task<ActionResult<DiscountCodeDto>> AddCode(Guid id, AddCodeRequest request, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, await codes.AddAsync(id, request, ct));

    /// <summary>CSV from the brand (multipart field "file"): code, optional validFrom, validTo, note, email (assign). dryRun=true only validates.</summary>
    [HttpPost("{id:guid}/codes/import")]
    [HasPermission(Permissions.CodesManage)]
    [RequestSizeLimit(CodeLimits.MaxCsvBytes + 64 * 1024)]
    public Task<CodeImportResultDto> ImportCodes(Guid id, IFormFile? file, [FromQuery] bool dryRun, CancellationToken ct)
    {
        if (file is null || file.Length == 0) throw new DomainException("csv.empty", "Choose a CSV file to import.");
        return codes.ImportAsync(id, file, dryRun, ct);
    }

    /// <summary>Generates unique codes from a pattern (# digit, ? letter, * either). dryRun=true previews.</summary>
    [HttpPost("{id:guid}/codes/generate")]
    [HasPermission(Permissions.CodesManage)]
    public Task<CodeImportResultDto> Generate(Guid id, GenerateCodesRequest request, [FromQuery] bool dryRun, CancellationToken ct) =>
        codes.GenerateAsync(id, request, dryRun, ct);

    /// <summary>One unique available code per member of a rate group who has no personal code in the program yet.</summary>
    [HttpPost("{id:guid}/codes/auto-assign")]
    [HasPermission(Permissions.CodesAssign)]
    public Task<AutoAssignResultDto> AutoAssign(Guid id, AutoAssignRequest request, CancellationToken ct) => codes.AutoAssignAsync(id, request, ct);

    /// <summary>Assignment history of the program (newest first).</summary>
    [HttpGet("{id:guid}/assignments")]
    [HasPermission(Permissions.CodesView)]
    public Task<PagedResult<CodeAssignmentDto>> Assignments(Guid id, [FromQuery] PageQuery query, CancellationToken ct) => codes.AssignmentsAsync(id, query, ct);

    // ---- sales

    /// <summary>Staff-entered sale (attributed to the code's holder on the order date unless userId is given). Someone else must approve it.</summary>
    [HttpPost("{id:guid}/sales")]
    [HasPermission(Permissions.CodesManage)]
    public async Task<ActionResult<CodeSaleDto>> AddSale(Guid id, AdminCreateSaleRequest request, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, await sales.AdminCreateAsync(id, request, ct));

    /// <summary>
    /// The brand's sales report (multipart "file": order id, code, amount, date, optional status/currency/discount): matches
    /// reported sales, flags differences, creates unclaimed sales for the code's holder, cancels/refunds. dryRun=true previews.
    /// </summary>
    [HttpPost("{id:guid}/sales/import")]
    [HasPermission(Permissions.CodesManage)]
    [RequestSizeLimit(CodeLimits.MaxCsvBytes + 64 * 1024)]
    public Task<SalesImportResultDto> ImportSales(Guid id, IFormFile? file, [FromQuery] bool dryRun, CancellationToken ct)
    {
        if (file is null || file.Length == 0) throw new DomainException("csv.empty", "Choose a CSV file to import.");
        return sales.ImportReportAsync(id, file, dryRun, ct);
    }
}

/// <summary>One discount code: details and assignment history, status/validity, assign / reassign / unassign.</summary>
[DeniedWhileImpersonating(WritesOnly = true)]
[ApiController]
[Route("api/v1/admin/discount-codes/{codeId:guid}")]
public sealed class DiscountCodesController(IDiscountCodesService codes) : ControllerBase
{
    [HttpGet]
    [HasPermission(Permissions.CodesView)]
    public Task<DiscountCodeDetailDto> Get(Guid codeId, CancellationToken ct) => codes.GetAsync(codeId, ct);

    /// <summary>Pause / resume (Available) / retire, validity and note. 409 concurrency.conflict / code.retired.</summary>
    [HttpPut]
    [HasPermission(Permissions.CodesManage)]
    public Task<DiscountCodeDto> Update(Guid codeId, UpdateCodeRequest request, CancellationToken ct) => codes.UpdateAsync(codeId, request, ct);

    /// <summary>Personal (userId) or shared with a rate group (groupId). 409 code.already_assigned unless "reassign": true; code.paused/expired/retired.</summary>
    [HttpPost("assign")]
    [HasPermission(Permissions.CodesAssign)]
    public Task<DiscountCodeDetailDto> Assign(Guid codeId, AssignCodeRequest request, CancellationToken ct) => codes.AssignAsync(codeId, request, ct);

    [HttpPost("unassign")]
    [HasPermission(Permissions.CodesAssign)]
    public Task<DiscountCodeDetailDto> Unassign(Guid codeId, UnassignCodeRequest request, CancellationToken ct) => codes.UnassignAsync(codeId, request, ct);
}

/// <summary>Code sales across programs: review queue, decisions (four-eyes), bulk approval of verified sales, refunds, export.</summary>
[DeniedWhileImpersonating(WritesOnly = true)]
[ApiController]
[Route("api/v1/admin/code-sales")]
public sealed class CodeSalesController(ICodeSalesService sales) : ControllerBase
{
    [HttpGet]
    [RequireAnyPermission(Permissions.CodesView, Permissions.SalesReview)]
    public Task<PagedResult<CodeSaleListItemDto>> List([FromQuery] CodeSaleQuery query, CancellationToken ct) => sales.ListAsync(query, ct);

    [HttpGet("{id:guid}")]
    [RequireAnyPermission(Permissions.CodesView, Permissions.SalesReview)]
    public Task<CodeSaleDto> Get(Guid id, CancellationToken ct) => sales.GetAsync(id, ct);

    /// <summary>
    /// Approve (commission to the ledger, caps/budget applied), Reject or RequestInfo (reason required). Never your own sale or one
    /// you entered/imported (403 code_sale.self_review / code_sale.four_eyes); 409 code_sale.already_decided, participant.not_active.
    /// </summary>
    [HttpPost("{id:guid}/decision")]
    [HasPermission(Permissions.SalesReview)]
    public Task<CodeSaleDto> Decide(Guid id, CodeSaleDecisionRequest request, CancellationToken ct) => sales.DecideAsync(id, request, ct);

    /// <summary>Approves up to 200 sales one by one (by default only those the brand's report matched); per-sale results.</summary>
    [HttpPost("bulk-approve")]
    [HasPermission(Permissions.SalesReview)]
    public Task<BulkDecisionResultDto> BulkApprove(BulkApproveSalesRequest request, CancellationToken ct) => sales.BulkApproveAsync(request, ct);

    /// <summary>Refund / cancellation: approved → Refunded with its commission reversed (clawback if paid); pending → Cancelled.</summary>
    [HttpPost("{id:guid}/refund")]
    [HasPermission(Permissions.SalesReverse)]
    public Task<CodeSaleDto> Refund(Guid id, RefundCodeSaleRequest request, CancellationToken ct) => sales.RefundAsync(id, request, ct);

    [HttpGet("export.csv")]
    [HasPermission(Permissions.CodesView)]
    public async Task<IActionResult> Export([FromQuery] CodeSaleQuery query, CancellationToken ct)
    {
        var (name, header, rows) = await sales.ExportAsync(query, ct);
        return Csv.File(name, header, rows);
    }
}

/// <summary>Uses, sales, discount and commissions per program, code, person or rate group (program currency); CSV export.</summary>
[ApiController]
[Route("api/v1/admin/code-reports")]
public sealed class CodeReportsController(ICodeReportsService reports) : ControllerBase
{
    [HttpGet]
    [HasPermission(Permissions.CodesView)]
    public Task<CodeReportDto> Get([FromQuery] CodeReportQuery query, CancellationToken ct) => reports.ReportAsync(query, ct);

    [HttpGet("export.csv")]
    [HasPermission(Permissions.CodesView)]
    public async Task<IActionResult> Export([FromQuery] CodeReportQuery query, CancellationToken ct)
    {
        var (name, header, rows) = await reports.ExportAsync(query, ct);
        return Csv.File(name, header, rows);
    }
}

/// <summary>The participant's own codes (copy, share link, terms, their stats) — never anyone else's.</summary>
[ApiController]
[Route("api/v1/me/codes")]
[HasPermission(Permissions.ParticipantPortal)]
public sealed class MeCodesController(ICodeSalesService sales) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<MyCodeDto>> List(CancellationToken ct) => sales.MyCodesAsync(ct);
}

/// <summary>Sales the participant reports with their codes. Writes claim money, so they are denied while impersonating.</summary>
[DeniedWhileImpersonating(WritesOnly = true)]
[ApiController]
[Route("api/v1/me/code-sales")]
[HasPermission(Permissions.ParticipantPortal)]
public sealed class MeCodeSalesController(ICodeSalesService sales) : ControllerBase
{
    [HttpGet]
    public Task<PagedResult<MyCodeSaleDto>> List([FromQuery] MyCodeSalesQuery query, CancellationToken ct) => sales.MySalesAsync(query, ct);

    [HttpGet("{id:guid}")]
    public Task<MyCodeSaleDto> Get(Guid id, CancellationToken ct) => sales.MySaleAsync(id, ct);

    /// <summary>
    /// Reports a sale (multipart/form-data; optional "proof" image). The code must be yours (or your group's) on the order date;
    /// 409 code_sale.duplicate_order when the order was already reported (the first report counts).
    /// </summary>
    [HttpPost]
    [EnableRateLimiting(RateLimitPolicies.Submissions)]
    [RequestSizeLimit(CodeLimits.MaxProofBytes + 2 * 1024 * 1024)]
    public async Task<ActionResult<MyCodeSaleDto>> Create([FromForm] CreateCodeSaleForm form, CancellationToken ct)
    {
        var created = await sales.CreateAsync(form, ct);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    }

    /// <summary>Edits a Pending sale, or answers a request for information (NeedsInfo → Pending). multipart/form-data.</summary>
    [HttpPut("{id:guid}")]
    [EnableRateLimiting(RateLimitPolicies.Submissions)]
    [RequestSizeLimit(CodeLimits.MaxProofBytes + 2 * 1024 * 1024)]
    public Task<MyCodeSaleDto> Update(Guid id, [FromForm] UpdateCodeSaleForm form, CancellationToken ct) => sales.UpdateAsync(id, form, ct);

    [HttpPost("{id:guid}/withdraw")]
    public Task<MyCodeSaleDto> Withdraw(Guid id, WithdrawCodeSaleRequest request, CancellationToken ct) => sales.WithdrawAsync(id, request, ct);
}
