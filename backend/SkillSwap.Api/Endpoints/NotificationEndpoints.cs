using System.Security.Claims;

namespace SkillSwap.Api.Endpoints;

public static class NotificationEndpoints
{
    public static void MapNotifications(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/notifications").WithTags("Notifications").RequireAuthorization();

        // GET /api/notifications?unreadOnly=false
        g.MapGet("", async (ClaimsPrincipal p, AppDbContext db, bool unreadOnly = false) =>
        {
            var me = p.GetUserId();
            var q = db.Notifications.AsNoTracking().Where(n => n.UserId == me);
            if (unreadOnly)
                q = q.Where(n => !n.IsRead);

            var items = await q.OrderByDescending(n => n.CreatedAt)
                .Take(50)
                .Select(n => new NotificationDto(n.Id, n.Type, n.Text, n.SwapId, n.IsRead, n.CreatedAt))
                .ToListAsync();

            return Results.Ok(items);
        });

        // POST /api/notifications/{id}/read
        g.MapPost("/{id:long}/read", async (long id, ClaimsPrincipal p, AppDbContext db) =>
        {
            var me = p.GetUserId();
            var n = await db.Notifications.FirstOrDefaultAsync(x => x.Id == id && x.UserId == me);
            if (n is null) return Results.NotFound();

            n.IsRead = true;
            await db.SaveChangesAsync();
            return Results.NoContent();
        });
    }
}
