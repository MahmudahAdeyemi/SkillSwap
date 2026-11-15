using System.Security.Claims;

namespace SkillSwap.Api.Endpoints;

public static class SearchEndpoints
{
    public static void MapSearch(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/search", async (ClaimsPrincipal p, AppDbContext db,
            UserMapper mapper, string? q) =>
        {
            var query = (q ?? "").Trim();
            if (query.Length == 0)
                return Results.Ok(new SearchResultDto("", [], [], []));

            var text = query.ToLowerInvariant();
            var me = p.Identity?.IsAuthenticated == true ? p.GetUserId() : (Guid?)null;

            var skills = await db.Skills.AsNoTracking()
                .Where(s => s.Name.ToLower().Contains(text) || s.Category.ToLower().Contains(text))
                .OrderBy(s => s.Name)
                .Take(12)
                .ToListAsync();

            var skillIds = skills.Select(s => s.Id).ToList();

            // People who offer a skill that matched the query.
            var teacherIds = await db.UserSkills.AsNoTracking()
                .Where(x => x.Type == SkillType.Offering && skillIds.Contains(x.SkillId) &&
                            (!me.HasValue || x.UserId != me.Value))
                .Select(x => new { x.UserId, x.SkillId })
                .Distinct()
                .ToListAsync();

            var teacherUsers = await mapper.GetManyAsync(teacherIds.Select(x => x.UserId));
            var skillDtos = skills.ToDictionary(s => s.Id, s => s.ToDto());

            var skillPeople = teacherIds
                .GroupBy(x => x.UserId)
                .Where(g => teacherUsers.ContainsKey(g.Key))
                .Select(g => new MatchDto(
                    teacherUsers[g.Key],
                    MatchKind.Partial,
                    60 + g.Count(),
                    g.Select(x => skillDtos.GetValueOrDefault(x.SkillId))
                        .Where(s => s is not null)
                        .Select(s => s!)
                        .Distinct()
                        .ToList(),
                    []))
                .ToList();

            // People whose name, bio, or location matches the query — not only people
            // who teach a matching skill.
            var namedIds = await db.Users.AsNoTracking()
                .Where(u => (!me.HasValue || u.Id != me.Value) &&
                            (u.Name.ToLower().Contains(text) ||
                             (u.Bio != null && u.Bio.ToLower().Contains(text)) ||
                             (u.Location != null && u.Location.ToLower().Contains(text))))
                .OrderBy(u => u.Name)
                .Take(12)
                .Select(u => u.Id)
                .ToListAsync();
            var namedUsers = await mapper.GetManyAsync(namedIds);
            var existingIds = skillPeople.Select(m => m.User.Id).ToHashSet();
            var nameMatches = namedIds
                .Where(id => !existingIds.Contains(id) && namedUsers.ContainsKey(id))
                .Select(id => new MatchDto(namedUsers[id], MatchKind.Partial, 50, [], []))
                .ToList();

            var people = skillPeople
                .Concat(nameMatches)
                .OrderByDescending(m => m.User.AverageRating)
                .ThenByDescending(m => m.Score)
                .Take(12)
                .ToList();

            var posts = await db.Posts.AsNoTracking()
                .Where(x => x.Content.ToLower().Contains(text) || x.Category.ToLower().Contains(text))
                .OrderByDescending(x => x.CreatedAt)
                .Take(5)
                .ToListAsync();

            var authors = await mapper.GetManyAsync(posts.Select(x => x.AuthorId));
            var postDtos = posts
                .Where(x => authors.ContainsKey(x.AuthorId))
                .Select(x => new CommunityPostDto(
                    x.Id, authors[x.AuthorId], x.Category, x.Content, x.ImageUrl,
                    x.LikesCount, x.CommentsCount, false, x.CreatedAt))
                .ToList();

            return Results.Ok(new SearchResultDto(query, skills.Select(s => s.ToDto()).ToList(), people, postDtos));
        })
        .WithTags("Search")
        .AllowAnonymous();
    }
}
