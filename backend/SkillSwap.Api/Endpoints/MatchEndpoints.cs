using System.Security.Claims;

namespace SkillSwap.Api.Endpoints;

public static class MatchEndpoints
{
    public static void MapMatches(this IEndpointRouteBuilder app)
    {
        // GET /api/matches?kind=mutual|partial|chain&page=1&pageSize=20
        app.MapGet("/api/matches", async (ClaimsPrincipal p, MatchingService matching,
            string? kind, int page = 1, int pageSize = 20) =>
        {
            var me = p.GetUserId();
            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 1, 50);
            kind = (kind ?? "mutual").Trim().ToLowerInvariant();

            if (kind == "chain")
                return Results.Ok(await matching.FindChainsAsync(me));

            if (kind != "mutual" && kind != "partial")
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["kind"] = new[] { "Use mutual, partial or chain." }
                });

            var all = await matching.FindMatchesAsync(me, kind == "mutual" ? MatchKind.Mutual : MatchKind.Partial);
            var items = all.Skip((page - 1) * pageSize).Take(pageSize).ToList();
            return Results.Ok(new PagedResult<MatchDto>(items, page, pageSize, all.Count));
        })
        .WithTags("Matching")
        .RequireAuthorization();
    }
}
