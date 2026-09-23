using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OptimizeAll.Api.Common.Http;
using OptimizeAll.Api.Common.Security;

namespace OptimizeAll.Api.Modules.Notifications;

/// <summary>The in-app notification center (any authenticated user).</summary>
[ApiController]
[Authorize]
[Route("api/v1/me")]
public sealed class MyNotificationsController(NotificationCenterService center, ICurrentUser currentUser) : ControllerBase
{
    [HttpGet("notifications")]
    public Task<PagedResult<NotificationDto>> List([FromQuery] NotificationListQuery query, CancellationToken ct) =>
        center.ListAsync(currentUser.Id, query, ct);

    [HttpGet("notifications/unread-count")]
    public Task<UnreadCountDto> UnreadCount(CancellationToken ct) => center.UnreadCountAsync(currentUser.Id, ct);

    [HttpPost("notifications/{id:guid}/read")]
    public async Task<IActionResult> MarkRead(Guid id, CancellationToken ct)
    {
        await center.MarkReadAsync(currentUser.Id, id, ct);
        return NoContent();
    }

    [HttpPost("notifications/read-all")]
    public Task<ReadAllResponse> MarkAllRead(CancellationToken ct) => center.MarkAllReadAsync(currentUser.Id, ct);

    [HttpGet("notification-preferences")]
    public Task<NotificationPreferencesDto> Preferences(CancellationToken ct) => center.GetPreferencesAsync(currentUser.Id, ct);

    [HttpPut("notification-preferences")]
    public Task<NotificationPreferencesDto> UpdatePreferences(UpdatePreferencesRequest request, CancellationToken ct) =>
        center.UpdatePreferencesAsync(currentUser.Id, request, ct);
}

/// <summary>Outbox monitoring for operators.</summary>
[ApiController]
[HasPermission(Permissions.JobsView)]
[Route("api/v1/admin/notifications/deliveries")]
public sealed class AdminNotificationDeliveriesController(NotificationCenterService center) : ControllerBase
{
    [HttpGet]
    public Task<PagedResult<DeliveryDto>> List([FromQuery] DeliveryQuery query, CancellationToken ct) => center.ListDeliveriesAsync(query, ct);

    [HttpPost("{id:guid}/retry")]
    public Task<DeliveryDto> Retry(Guid id, CancellationToken ct) => center.RetryDeliveryAsync(id, ct);
}
