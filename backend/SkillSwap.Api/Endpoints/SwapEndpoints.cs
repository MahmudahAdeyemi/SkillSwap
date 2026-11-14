using System.Security.Claims;

namespace SkillSwap.Api.Endpoints;

public static class SwapEndpoints
{
    public static void MapSwaps(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/swaps").WithTags("Swaps").RequireAuthorization();

        // ---------- Propose ----------
        g.MapPost("", async (CreateSwapRequest req, ClaimsPrincipal p, AppDbContext db, SwapMapper mapper) =>
        {
            var me = p.GetUserId();

            if (req.PartnerId == me)
                return Results.Problem(statusCode: 422, title: "You can't swap with yourself.");
            if (req.SkillIWillTeachId == req.SkillIWillLearnId)
                return Results.Problem(statusCode: 422, title: "Pick two different skills for a swap.");
            if (!await db.Users.AnyAsync(u => u.Id == req.PartnerId))
                return Results.Problem(statusCode: 404, title: "Partner not found.");

            if (!await db.UserSkills.AnyAsync(x => x.UserId == me && x.SkillId == req.SkillIWillTeachId && x.Type == SkillType.Offering))
                return Results.Problem(statusCode: 422, title: "You can only teach a skill you list as Offering.");
            if (!await db.UserSkills.AnyAsync(x => x.UserId == req.PartnerId && x.SkillId == req.SkillIWillLearnId && x.Type == SkillType.Offering))
                return Results.Problem(statusCode: 422, title: "Your partner doesn't offer that skill.");

            var duplicate = await db.Swaps.AnyAsync(s =>
                (s.Status == SwapStatus.Proposed || s.Status == SwapStatus.Active) &&
                ((s.ProposerId == me && s.ReceiverId == req.PartnerId &&
                  s.ProposerTeachesId == req.SkillIWillTeachId && s.ReceiverTeachesId == req.SkillIWillLearnId) ||
                 (s.ProposerId == req.PartnerId && s.ReceiverId == me &&
                  s.ProposerTeachesId == req.SkillIWillLearnId && s.ReceiverTeachesId == req.SkillIWillTeachId)));
            if (duplicate)
                return Results.Problem(statusCode: 409, title: "There is already an open swap between you for these skills.");

            var swap = new Swap
            {
                ProposerId = me,
                ReceiverId = req.PartnerId,
                ProposerTeachesId = req.SkillIWillTeachId,
                ReceiverTeachesId = req.SkillIWillLearnId,
                HoursEach = req.HoursEach,
                Message = req.Message?.Trim(),
                Status = SwapStatus.Proposed
            };
            db.Swaps.Add(swap);

            db.UserEvents.Add(new UserEvent
            {
                UserId = me,
                Type = EventType.ProposeSwap,
                SkillId = req.SkillIWillLearnId,
                TargetUserId = req.PartnerId,
                Weight = EventWeights.For(EventType.ProposeSwap)
            });

            await db.SaveChangesAsync();

            var loaded = await SwapRules.QueryWithSkills(db).FirstAsync(s => s.Id == swap.Id);
            return Results.Created($"/api/swaps/{swap.Id}", await mapper.ToDtoAsync(loaded));
        })
        .Validate<CreateSwapRequest>();

        // ---------- List mine ----------
        g.MapGet("", async (ClaimsPrincipal p, AppDbContext db, SwapMapper mapper,
            string? status, int page = 1, int pageSize = 20) =>
        {
            var me = p.GetUserId();
            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 1, 50);

            var q = SwapRules.QueryWithSkills(db).AsNoTracking()
                .Where(s => s.ProposerId == me || s.ReceiverId == me);

            if (!string.IsNullOrWhiteSpace(status))
            {
                if (!Enum.TryParse<SwapStatus>(status, ignoreCase: true, out var parsed))
                    return Results.ValidationProblem(new Dictionary<string, string[]>
                    {
                        ["status"] = new[] { "Use Proposed, Active, Completed, Declined or Cancelled." }
                    });
                q = q.Where(s => s.Status == parsed);
            }

            var total = await q.CountAsync();
            var rows = await q.OrderByDescending(s => s.CreatedAt)
                .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();

            return Results.Ok(new PagedResult<SwapDto>(await mapper.ToDtosAsync(rows), page, pageSize, total));
        });

        // ---------- Details ----------
        g.MapGet("/{id:guid}", async (Guid id, ClaimsPrincipal p, AppDbContext db, SwapMapper mapper) =>
        {
            var (swap, error) = await SwapRules.LoadForParticipantAsync(db, id, p.GetUserId());
            if (error is not null) return error;
            return Results.Ok(await mapper.ToDtoAsync(swap!));
        });

        // ---------- Accept (receiver only) ----------
        g.MapPost("/{id:guid}/accept", async (Guid id, ClaimsPrincipal p, AppDbContext db, SwapMapper mapper) =>
        {
            var me = p.GetUserId();
            var (swap, error) = await SwapRules.LoadForParticipantAsync(db, id, me);
            if (error is not null) return error;

            if (swap!.ReceiverId != me)
                return Results.Problem(statusCode: 403, title: "Only the person who received the proposal can accept it.");
            if (swap.Status != SwapStatus.Proposed)
                return Results.Problem(statusCode: 409, title: $"This swap is {swap.Status} and can no longer be accepted.");

            swap.Status = SwapStatus.Active;
            swap.AcceptedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
            return Results.Ok(await mapper.ToDtoAsync(swap));
        });

        // ---------- Decline (receiver only) ----------
        g.MapPost("/{id:guid}/decline", async (Guid id, ClaimsPrincipal p, AppDbContext db, SwapMapper mapper) =>
        {
            var me = p.GetUserId();
            var (swap, error) = await SwapRules.LoadForParticipantAsync(db, id, me);
            if (error is not null) return error;

            if (swap!.ReceiverId != me)
                return Results.Problem(statusCode: 403, title: "Only the person who received the proposal can decline it.");
            if (swap.Status != SwapStatus.Proposed)
                return Results.Problem(statusCode: 409, title: $"This swap is {swap.Status} and can no longer be declined.");

            swap.Status = SwapStatus.Declined;
            await db.SaveChangesAsync();
            return Results.Ok(await mapper.ToDtoAsync(swap));
        });

        // ---------- Cancel (either party, while Proposed or Active) ----------
        g.MapPost("/{id:guid}/cancel", async (Guid id, ClaimsPrincipal p, AppDbContext db, SwapMapper mapper) =>
        {
            var (swap, error) = await SwapRules.LoadForParticipantAsync(db, id, p.GetUserId());
            if (error is not null) return error;

            if (swap!.Status != SwapStatus.Proposed && swap.Status != SwapStatus.Active)
                return Results.Problem(statusCode: 409, title: $"This swap is {swap.Status} and can't be cancelled.");

            swap.Status = SwapStatus.Cancelled;
            await db.SaveChangesAsync();
            return Results.Ok(await mapper.ToDtoAsync(swap));
        });

        // ---------- Complete (each side calls once; done when both have) ----------
        g.MapPost("/{id:guid}/complete", async (Guid id, ClaimsPrincipal p, AppDbContext db, SwapMapper mapper) =>
        {
            var me = p.GetUserId();
            var (swap, error) = await SwapRules.LoadForParticipantAsync(db, id, me);
            if (error is not null) return error;

            if (swap!.Status != SwapStatus.Active)
                return Results.Problem(statusCode: 409, title: $"Only an Active swap can be completed. This one is {swap.Status}.");

            var isProposer = swap.ProposerId == me;
            if ((isProposer && swap.ProposerCompleted) || (!isProposer && swap.ReceiverCompleted))
                return Results.Problem(statusCode: 409, title: "You already marked this swap complete. Waiting for your partner.");

            if (isProposer) swap.ProposerCompleted = true;
            else swap.ReceiverCompleted = true;

            if (swap.ProposerCompleted && swap.ReceiverCompleted)
            {
                swap.Status = SwapStatus.Completed;
                swap.CompletedAt = DateTime.UtcNow;

                // Feeds the discover page: each person "learned" the skill the other taught.
                db.UserEvents.AddRange(
                    new UserEvent
                    {
                        UserId = swap.ProposerId, Type = EventType.CompleteSwap,
                        SkillId = swap.ReceiverTeachesId, TargetUserId = swap.ReceiverId,
                        Weight = EventWeights.For(EventType.CompleteSwap)
                    },
                    new UserEvent
                    {
                        UserId = swap.ReceiverId, Type = EventType.CompleteSwap,
                        SkillId = swap.ProposerTeachesId, TargetUserId = swap.ProposerId,
                        Weight = EventWeights.For(EventType.CompleteSwap)
                    });
            }

            await db.SaveChangesAsync();
            return Results.Ok(await mapper.ToDtoAsync(swap));
        });
    }
}

/// <summary>Shared loading + authorization for swap endpoints.</summary>
public static class SwapRules
{
    public static IQueryable<Swap> QueryWithSkills(AppDbContext db) =>
        db.Swaps.Include(s => s.ProposerTeaches).Include(s => s.ReceiverTeaches);

    public static async Task<(Swap? Swap, IResult? Error)> LoadForParticipantAsync(AppDbContext db, Guid id, Guid me)
    {
        var swap = await QueryWithSkills(db).FirstOrDefaultAsync(s => s.Id == id);
        if (swap is null)
            return (null, Results.Problem(statusCode: 404, title: "Swap not found."));
        if (swap.ProposerId != me && swap.ReceiverId != me)
            return (null, Results.Problem(statusCode: 403, title: "You are not part of this swap."));
        return (swap, null);
    }
}
