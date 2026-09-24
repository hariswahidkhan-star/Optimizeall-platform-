using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OptimizeAll.Api.Common.Security;

namespace OptimizeAll.Api.Modules.Accounts;

/// <summary>The signed-in user's own profile (any authenticated user, staff included).</summary>
[ApiController]
[Authorize]
[Route("api/v1/me/profile")]
public sealed class ProfileController(IProfileService profiles, ICurrentUser currentUser) : ControllerBase
{
    [HttpGet]
    public Task<ProfileDto> Get(CancellationToken ct) => profiles.GetProfileAsync(currentUser.Id, ct);

    [HttpPut]
    public Task<ProfileDto> Update(UpdateProfileRequest request, CancellationToken ct) =>
        profiles.UpdateProfileAsync(currentUser.Id, request, ct);
}

/// <summary>Where the participant is paid. The raw destination is write-only; responses carry a masked hint.</summary>
[ApiController]
[HasPermission(Permissions.ParticipantPortal)]
[Route("api/v1/me/payout-profile")]
[DeniedWhileImpersonating(WritesOnly = true)]
public sealed class PayoutProfileController(IProfileService profiles, ICurrentUser currentUser) : ControllerBase
{
    [HttpGet]
    public Task<PayoutProfileDto> Get(CancellationToken ct) => profiles.GetPayoutProfileAsync(currentUser.Id, ct);

    [HttpPut]
    public Task<PayoutProfileDto> Update(UpdatePayoutProfileRequest request, CancellationToken ct) =>
        profiles.UpdatePayoutProfileAsync(currentUser.Id, request, ct);
}

[ApiController]
[HasPermission(Permissions.ParticipantPortal)]
[Route("api/v1/me")]
public sealed class HomeController(IHomeService home, ICurrentUser currentUser) : ControllerBase
{
    /// <summary>Dynamic participant homepage: state, onboarding, banners, announcements and counters.</summary>
    [HttpGet("home")]
    public Task<HomeDto> Get(CancellationToken ct) => home.GetHomeAsync(currentUser.Id, ct);

    /// <summary>Dismisses a Manual onboarding step (idempotent). Other steps complete automatically.</summary>
    [HttpPost("onboarding/{stepId:guid}/complete")]
    public Task<OnboardingCompleteResponse> Complete(Guid stepId, CancellationToken ct) =>
        home.CompleteManualStepAsync(currentUser.Id, stepId, ct);
}
