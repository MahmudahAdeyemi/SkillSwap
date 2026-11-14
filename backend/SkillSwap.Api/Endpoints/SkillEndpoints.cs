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
