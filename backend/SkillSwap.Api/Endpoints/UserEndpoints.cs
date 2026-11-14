using System.Security.Claims;

namespace SkillSwap.Api.Endpoints;

public static class UserEndpoints
{
    public static void MapUsers(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/users").WithTags("Users").RequireAuthorization();

        // ---------- Profiles ----------
        g.MapGet("/{id:guid}", async (Guid id, ClaimsPrincipal p, AppDbContext db, UserMapper mapper) =>
        {
            var user = await mapper.GetAsync(id);
            if (user is null) return Results.NotFound();

            var me = p.GetUserId();
            var skills = await db.UserSkills.AsNoTracking()
                .Include(x => x.Skill).Where(x => x.UserId == id).ToListAsync();
            var isFollowing = await db.Follows.AsNoTracking()
                .AnyAsync(f => f.FollowerId == me && f.FollowingId == id);

            return Results.Ok(new PublicProfileDto(
                user,
                skills.Where(x => x.Type == SkillType.Offering).Select(x => x.ToDto()).ToList(),
                skills.Where(x => x.Type == SkillType.Wanted).Select(x => x.ToDto()).ToList(),
                isFollowing));
        });

        g.MapPost("/{id:guid}/follow", async (Guid id, ClaimsPrincipal p, AppDbContext db) =>
        {
            var me = p.GetUserId();
            if (id == me)
                return Results.Problem(statusCode: 422, title: "You can't follow yourself.");
            if (!await db.Users.AnyAsync(u => u.Id == id))
                return Results.Problem(statusCode: 404, title: "Member not found.");

            if (!await db.Follows.AnyAsync(f => f.FollowerId == me && f.FollowingId == id))
            {
                db.Follows.Add(new Follow { FollowerId = me, FollowingId = id });
                await db.SaveChangesAsync();
            }
            return Results.NoContent();
        });

        g.MapDelete("/{id:guid}/follow", async (Guid id, ClaimsPrincipal p, AppDbContext db) =>
        {
            var me = p.GetUserId();
            var follow = await db.Follows.FindAsync(me, id);
            if (follow is not null)
            {
                db.Follows.Remove(follow);
                await db.SaveChangesAsync();
            }
            return Results.NoContent();
        });

        g.MapPut("/me", async (UpdateProfileRequest req, ClaimsPrincipal p, AppDbContext db, UserMapper mapper) =>
        {
            var uid = p.GetUserId();
            var user = await db.Users.FindAsync(uid);
            if (user is null) return Results.NotFound();

            user.Name = req.Name.Trim();
            user.Bio = req.Bio?.Trim();
            user.Location = req.Location?.Trim();
            user.TimeZone = req.TimeZone?.Trim();
            user.AvatarUrl = req.AvatarUrl?.Trim();
            await db.SaveChangesAsync();

            return Results.Ok(await mapper.GetAsync(uid));
        })
        .Validate<UpdateProfileRequest>();

        g.MapGet("/{id:guid}/reviews", async (Guid id, AppDbContext db, UserMapper mapper, int page = 1, int pageSize = 10) =>
        {
            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 1, 50);

            var q = db.Reviews.AsNoTracking().Where(r => r.RevieweeId == id);
            var total = await q.CountAsync();
            var rows = await q.OrderByDescending(r => r.CreatedAt)
                .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();

            var reviewers = await mapper.GetManyAsync(rows.Select(r => r.ReviewerId));
            var items = rows
                .Select(r => new ReviewDto(r.Id, r.SwapId, reviewers[r.ReviewerId], r.Rating, r.Comment, r.CreatedAt))
                .ToList();

            return Results.Ok(new PagedResult<ReviewDto>(items, page, pageSize, total));
        });

        // ---------- My skills ----------
        g.MapGet("/me/skills", async (ClaimsPrincipal p, AppDbContext db) =>
        {
            var uid = p.GetUserId();
            var skills = await db.UserSkills.AsNoTracking()
                .Include(x => x.Skill).Where(x => x.UserId == uid).ToListAsync();

            return Results.Ok(new MySkillsDto(
                skills.Where(x => x.Type == SkillType.Offering).Select(x => x.ToDto()).ToList(),
                skills.Where(x => x.Type == SkillType.Wanted).Select(x => x.ToDto()).ToList()));
        });

        g.MapPost("/me/skills", async (AddUserSkillRequest req, ClaimsPrincipal p, AppDbContext db) =>
        {
            var uid = p.GetUserId();

            var skill = await db.Skills.FindAsync(req.SkillId);
            if (skill is null)
                return Results.Problem(statusCode: 404, title: "Skill not found.");

            if (await db.UserSkills.AnyAsync(x => x.UserId == uid && x.SkillId == req.SkillId))
                return Results.Problem(statusCode: 409,
                    title: "You already listed this skill. Remove it first to change its type.");

            var us = new UserSkill
            {
                UserId = uid,
                SkillId = req.SkillId,
                Type = req.Type,
                Level = req.Level,
                Notes = req.Notes?.Trim()
            };
            db.UserSkills.Add(us);

            if (req.Type == SkillType.Wanted)
                db.UserEvents.Add(new UserEvent
                {
                    UserId = uid,
                    Type = EventType.AddWanted,
                    SkillId = req.SkillId,
                    Weight = EventWeights.For(EventType.AddWanted)
                });

            await db.SaveChangesAsync();
            us.Skill = skill;
            return Results.Created($"/api/users/me/skills/{us.Id}", us.ToDto());
        })
        .Validate<AddUserSkillRequest>();

        g.MapPut("/me/skills/{id:int}", async (int id, UpdateUserSkillRequest req, ClaimsPrincipal p, AppDbContext db) =>
        {
            var uid = p.GetUserId();
            var us = await db.UserSkills.Include(x => x.Skill)
                .FirstOrDefaultAsync(x => x.Id == id && x.UserId == uid);
            if (us is null) return Results.NotFound();

            us.Level = req.Level;
            us.Notes = req.Notes?.Trim();
            await db.SaveChangesAsync();
            return Results.Ok(us.ToDto());
        })
        .Validate<UpdateUserSkillRequest>();

        g.MapDelete("/me/skills/{id:int}", async (int id, ClaimsPrincipal p, AppDbContext db) =>
        {
            var uid = p.GetUserId();
            var us = await db.UserSkills.FirstOrDefaultAsync(x => x.Id == id && x.UserId == uid);
            if (us is null) return Results.NotFound();

            db.UserSkills.Remove(us);
            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        // ---------- Onboarding (cold start for discover) ----------
        g.MapPost("/me/onboarding", async (OnboardingRequest req, ClaimsPrincipal p, AppDbContext db) =>
        {
            var uid = p.GetUserId();
            var validIds = (await db.Skills.Select(s => s.Id).ToListAsync()).ToHashSet();
            var taken = (await db.UserSkills.Where(x => x.UserId == uid).Select(x => x.SkillId).ToListAsync()).ToHashSet();

            foreach (var id in req.InterestSkillIds.Distinct().Where(i => validIds.Contains(i) && !taken.Contains(i)))
            {
                taken.Add(id);
                db.UserSkills.Add(new UserSkill { UserId = uid, SkillId = id, Type = SkillType.Wanted, Level = SkillLevel.Beginner });
                db.UserEvents.Add(new UserEvent
                {
                    UserId = uid, Type = EventType.AddWanted, SkillId = id,
                    Weight = EventWeights.For(EventType.AddWanted)
                });
            }

            foreach (var id in (req.OfferingSkillIds ?? new()).Distinct().Where(i => validIds.Contains(i) && !taken.Contains(i)))
            {
                taken.Add(id);
                db.UserSkills.Add(new UserSkill { UserId = uid, SkillId = id, Type = SkillType.Offering, Level = SkillLevel.Intermediate });
            }

            await db.SaveChangesAsync();
            return Results.NoContent();
        })
        .Validate<OnboardingRequest>();

        g.MapGet("/me/interests", async (ClaimsPrincipal p, InterestService interests) =>
            Results.Ok(await interests.GetTopAsync(p.GetUserId(), 8)));
    }
}
