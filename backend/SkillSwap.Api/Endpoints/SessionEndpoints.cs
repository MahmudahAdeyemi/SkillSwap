using System.Security.Claims;

namespace SkillSwap.Api.Endpoints;

public static class SessionEndpoints
{
    public static void MapSessions(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api").WithTags("Sessions").RequireAuthorization();

        // GET /api/swaps/{swapId}/sessions
        g.MapGet("/swaps/{swapId:guid}/sessions", async (Guid swapId, ClaimsPrincipal p,
            AppDbContext db, UserMapper mapper) =>
        {
            var me = p.GetUserId();
            var (swap, error) = await SwapRules.LoadForParticipantAsync(db, swapId, me);
            if (error is not null) return error;

            var sessions = await db.Sessions.AsNoTracking()
                .Include(s => s.Skill)
                .Where(s => s.SwapId == swapId)
                .OrderBy(s => s.ScheduledAt)
                .ToListAsync();

            var teacherIds = sessions.Select(s => s.TeacherId).Distinct();
            var teachers = await mapper.GetManyAsync(teacherIds);

            var dtos = sessions.Select(s => new SessionDto(
                s.Id,
                s.SwapId,
                teachers[s.TeacherId],
                s.Skill.ToDto(),
                s.ScheduledAt,
                s.DurationMinutes,
                s.Status,
                s.MeetingLink,
                s.Notes
            )).ToList();

            return Results.Ok(dtos);
        });

        // POST /api/swaps/{swapId}/sessions
        g.MapPost("/swaps/{swapId:guid}/sessions", async (Guid swapId, CreateSessionRequest req,
            ClaimsPrincipal p, AppDbContext db, UserMapper mapper) =>
        {
            var me = p.GetUserId();
            var (swap, error) = await SwapRules.LoadForParticipantAsync(db, swapId, me);
            if (error is not null) return error;

            if (swap!.Status != SwapStatus.Active)
                return Results.Problem(statusCode: 409, title: "Sessions can only be scheduled for Active swaps.");

            if (req.TeacherId != swap.ProposerId && req.TeacherId != swap.ReceiverId)
                return Results.Problem(statusCode: 422, title: "Teacher must be a participant in the swap.");

            if (req.SkillId != swap.ProposerTeachesId && req.SkillId != swap.ReceiverTeachesId)
                return Results.Problem(statusCode: 422, title: "Skill must be one of the agreed skills in the swap.");

            var skill = await db.Skills.FindAsync(req.SkillId);
            if (skill is null) return Results.NotFound();

            var session = new LessonSession
            {
                SwapId = swapId,
                TeacherId = req.TeacherId,
                SkillId = req.SkillId,
                ScheduledAt = req.ScheduledAt,
                DurationMinutes = req.DurationMinutes,
                MeetingLink = req.MeetingLink?.Trim(),
                Status = SessionStatus.Scheduled
            };
            db.Sessions.Add(session);

            var otherId = req.TeacherId == me
                ? (swap.ProposerId == me ? swap.ReceiverId : swap.ProposerId)
                : req.TeacherId;

            db.Notifications.Add(new Notification
            {
                UserId = otherId,
                Type = NotificationType.SessionScheduled,
                Text = $"New session scheduled for {session.ScheduledAt:g}",
                SwapId = swapId,
                IsRead = false
            });

            await db.SaveChangesAsync();

            var teacherDto = (await mapper.GetAsync(req.TeacherId))!;
            return Results.Created($"/api/sessions/{session.Id}",
                new SessionDto(session.Id, session.SwapId, teacherDto, skill.ToDto(),
                    session.ScheduledAt, session.DurationMinutes, session.Status, session.MeetingLink, session.Notes));
        })
        .Validate<CreateSessionRequest>();

        // PATCH /api/sessions/{id}
        g.MapPatch("/sessions/{id:guid}", async (Guid id, UpdateSessionRequest req,
            ClaimsPrincipal p, AppDbContext db, UserMapper mapper) =>
        {
            var me = p.GetUserId();
            var session = await db.Sessions.Include(s => s.Skill).FirstOrDefaultAsync(s => s.Id == id);
            if (session is null) return Results.NotFound();

            var (swap, error) = await SwapRules.LoadForParticipantAsync(db, session.SwapId, me);
            if (error is not null) return error;

            if (req.ScheduledAt.HasValue) session.ScheduledAt = req.ScheduledAt.Value;
            if (req.Status.HasValue) session.Status = req.Status.Value;
            if (req.Notes is not null) session.Notes = req.Notes.Trim();
            if (req.MeetingLink is not null) session.MeetingLink = req.MeetingLink.Trim();

            await db.SaveChangesAsync();

            var teacherDto = (await mapper.GetAsync(session.TeacherId))!;
            return Results.Ok(new SessionDto(session.Id, session.SwapId, teacherDto, session.Skill.ToDto(),
                session.ScheduledAt, session.DurationMinutes, session.Status, session.MeetingLink, session.Notes));
        })
        .Validate<UpdateSessionRequest>();
    }
}
