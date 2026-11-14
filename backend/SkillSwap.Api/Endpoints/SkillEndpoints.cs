using System.Security.Claims;

namespace SkillSwap.Api.Endpoints;

public static class SkillEndpoints
{
    public static void MapSkills(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/skills").WithTags("Skills");

        // GET /api/skills?query=&category=&page=&pageSize=
        g.MapGet("", async (AppDbContext db, string? query, string? category, int page = 1, int pageSize = 20) =>
        {
            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 1, 50);

            var q = db.Skills.AsNoTracking().AsQueryable();
            if (!string.IsNullOrWhiteSpace(query))
            {
                var s = query.Trim().ToLower();
                q = q.Where(x => x.Name.ToLower().Contains(s));
            }
            if (!string.IsNullOrWhiteSpace(category))
            {
                var c = category.Trim().ToLower();
                q = q.Where(x => x.Category.ToLower() == c);
            }

            var total = await q.CountAsync();
            var items = await q.OrderBy(x => x.Name)
                .Skip((page - 1) * pageSize).Take(pageSize)
                .Select(x => new SkillDto(x.Id, x.Name, x.Category))
                .ToListAsync();

            return Results.Ok(new PagedResult<SkillDto>(items, page, pageSize, total));
        })
        .AllowAnonymous();

        g.MapGet("/categories", async (AppDbContext db) =>
            Results.Ok(await db.Skills.Select(s => s.Category).Distinct().OrderBy(c => c).ToListAsync()))
        .AllowAnonymous();

        // GET /api/skills/{id} - everything the Skill Details screen needs.
        g.MapGet("/{id:int}", async (int id, AppDbContext db, UserMapper mapper) =>
        {
            var skill = await db.Skills.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id);
            if (skill is null) return Results.NotFound();

            var teacherIds = await db.UserSkills.AsNoTracking()
                .Where(x => x.SkillId == id && x.Type == SkillType.Offering)
                .Select(x => x.UserId)
                .Distinct()
                .ToListAsync();

            var learnerIds = await db.UserSkills.AsNoTracking()
                .Where(x => x.SkillId == id && x.Type == SkillType.Wanted)
                .Select(x => x.UserId)
                .Distinct()
                .ToListAsync();

            var wantedCount = await db.UserSkills.CountAsync(x => x.SkillId == id && x.Type == SkillType.Wanted);

            var userMap = await mapper.GetManyAsync(teacherIds.Concat(learnerIds));
            var teachers = teacherIds
                .Where(userMap.ContainsKey)
                .Select(id => userMap[id])
                .OrderByDescending(u => u.AverageRating)
                .ThenByDescending(u => u.CompletedSwaps)
                .Take(8)
                .ToList();
            var learners = learnerIds
                .Where(userMap.ContainsKey)
                .Select(id => userMap[id])
                .OrderByDescending(u => u.JoinedAt)
                .Take(8)
                .ToList();

            var related = await db.Skills.AsNoTracking()
                .Where(s => s.Category == skill.Category && s.Id != skill.Id)
                .OrderBy(s => s.Name)
                .Take(8)
                .Select(s => s.ToDto())
                .ToListAsync();

            var post = await db.Posts.AsNoTracking()
                .Where(p => p.Content.ToLower().Contains(skill.Name.ToLower()))
                .OrderByDescending(p => p.CreatedAt)
                .FirstOrDefaultAsync();
            post ??= await db.Posts.AsNoTracking()
                .OrderByDescending(p => p.CreatedAt)
                .FirstOrDefaultAsync();

            CommunityPostDto? discussion = null;
            if (post is not null)
            {
                var author = await mapper.GetAsync(post.AuthorId);
                if (author is not null)
                    discussion = new CommunityPostDto(post.Id, author, post.Category, post.Content,
                        post.ImageUrl, post.LikesCount, post.CommentsCount, false, post.CreatedAt);
            }

            return Results.Ok(new SkillDetailDto(
                skill.ToDto(),
                teacherIds.Count,
                learnerIds.Count,
                wantedCount,
                teachers,
                learners,
                related,
                discussion));
        })
        .AllowAnonymous();

        g.MapPost("", async (CreateSkillRequest req, AppDbContext db) =>
        {
            var name = req.Name.Trim();
            var lower = name.ToLower();
            if (await db.Skills.AnyAsync(s => s.Name.ToLower() == lower))
                return Results.Problem(statusCode: 409, title: "That skill already exists.");

            var skill = new Skill { Name = name, Category = req.Category.Trim() };
            db.Skills.Add(skill);
            await db.SaveChangesAsync();
            return Results.Created($"/api/skills/{skill.Id}", skill.ToDto());
        })
        .RequireAuthorization()
        .Validate<CreateSkillRequest>();
    }
}
