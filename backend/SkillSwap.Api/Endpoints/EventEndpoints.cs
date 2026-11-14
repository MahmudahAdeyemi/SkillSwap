using System.Security.Claims;

namespace SkillSwap.Api.Endpoints;

public static class EventEndpoints
{
    public static void MapEvents(this IEndpointRouteBuilder app)
    {
        // Fire-and-forget behavior tracking from the client (search, profile view, ...).
        // Weight is decided server-side so clients can't inflate it.
        app.MapPost("/api/events", async (TrackEventRequest req, ClaimsPrincipal p, AppDbContext db) =>
        {
            var skillId = req.SkillId;

            // A plain search ("graphic design") gets linked to the skill if the name matches exactly.
            if (skillId is null && !string.IsNullOrWhiteSpace(req.Query))
            {
                var q = req.Query.Trim().ToLower();
                skillId = await db.Skills.Where(s => s.Name.ToLower() == q)
                    .Select(s => (int?)s.Id).FirstOrDefaultAsync();
            }

            db.UserEvents.Add(new UserEvent
            {
                UserId = p.GetUserId(),
                Type = req.Type,
                SkillId = skillId,
                TargetUserId = req.TargetUserId,
                Query = req.Query?.Trim(),
                Weight = EventWeights.For(req.Type)
            });
            await db.SaveChangesAsync();
            return Results.Accepted();
        })
        .WithTags("Discovery")
        .RequireAuthorization()
        .Validate<TrackEventRequest>();
    }
}
