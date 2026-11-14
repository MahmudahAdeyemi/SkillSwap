namespace SkillSwap.Api.Endpoints;

public static class ImpactEndpoints
{
    public static void MapImpact(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/stats/impact", async (AppDbContext db) =>
        {
            var totalUsers = await db.Users.CountAsync();
            var activeSwaps = await db.Swaps.CountAsync(s => s.Status == SwapStatus.Active);
            var completedSwaps = await db.Swaps.CountAsync(s => s.Status == SwapStatus.Completed);

            var hoursCompleted = await db.Swaps
                .Where(s => s.Status == SwapStatus.Completed)
                .SumAsync(s => s.HoursEach * 2);

            const decimal assumedHourlyRate = 50m;
            var estimatedSavings = hoursCompleted * assumedHourlyRate;

            var topSkills = await db.UserSkills
                .GroupBy(us => us.Skill.Name)
                .OrderByDescending(g => g.Count())
                .Take(5)
                .Select(g => new SkillCountDto(g.Key, g.Count()))
                .ToListAsync();

            return Results.Ok(new ImpactStatsDto(
                totalUsers,
                activeSwaps,
                completedSwaps,
                hoursCompleted,
                estimatedSavings,
                "USD",
                topSkills));
        })
        .WithTags("Impact")
        .AllowAnonymous();
    }
}
