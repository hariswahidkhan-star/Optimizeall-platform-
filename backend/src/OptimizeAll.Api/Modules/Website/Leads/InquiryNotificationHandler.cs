using Microsoft.EntityFrameworkCore;
using OptimizeAll.Api.Common.Events;
using OptimizeAll.Api.Common.Notifications;
using OptimizeAll.Api.Common.Security;
using OptimizeAll.Domain.Events;
using OptimizeAll.Domain.Identity;
using OptimizeAll.Domain.Notifications;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Modules.Website.Leads;

/// <summary>Web paths the Website module links to from notifications (agency portal inbox).</summary>
public static class WebsiteLinks
{
    public const string InquiryNotificationType = "website.inquiry";

    public static string Inquiry(Guid id) => $"/agency/website/inquiries/{id}";
}

/// <summary>
/// Stages an in-app notification for every active staff member who handles leads (<c>site.manage</c> or
/// <c>crm.manage</c>) when a website inquiry arrives. Idempotent: a second delivery of the same event adds nothing.
/// </summary>
public sealed class InquiryNotificationHandler(AppDbContext db, INotificationService notifications, IPermissionDirectory directory)
    : IEventHandler<WebsiteInquiryReceived>
{
    private static readonly string[] RecipientPermissions = { Permissions.SiteManage, Permissions.CrmManage };

    public async Task HandleAsync(WebsiteInquiryReceived e, CancellationToken ct)
    {
        var link = WebsiteLinks.Inquiry(e.InquiryId);
        if (await db.Set<Notification>().AnyAsync(n => n.Type == WebsiteLinks.InquiryNotificationType && n.LinkUrl == link, ct)) return;

        // Built-in or custom-role holders of either permission.
        var recipients = await (await directory.UsersWithAnyPermissionAsync(RecipientPermissions, ct))
            .Where(u => u.Status == UserStatus.Active)
            .OrderBy(u => u.CreatedAt).Select(u => u.Id).Take(100).ToListAsync(ct);
        if (recipients.Count == 0) return;

        var kind = e.InquiryType switch
        {
            "Audit" => "free audit request",
            "Quote" => "quote request",
            "Consultation" => "consultation booking",
            _ => "contact message",
        };
        var who = e.Company is null ? e.Name : $"{e.Name} ({e.Company})";
        var services = e.ServiceSlugs.Count > 0 ? $" Interested in: {string.Join(", ", e.ServiceSlugs)}." : string.Empty;
        foreach (var userId in recipients)
        {
            await notifications.StageAsync(new NotificationRequest(userId, WebsiteLinks.InquiryNotificationType,
                $"New {kind} from {who}", $"{e.Email} sent a {kind} through the website.{services}", link), ct);
        }
        await db.SaveChangesAsync(ct);
    }
}
