namespace SkillSwap.Api.Services;

/// <summary>
/// Turns a user's event history into interest scores per skill.
/// score = sum(weight * 0.5^(ageInDays / 7))  -> halves every week.
/// The discover page will build its shelves from this.
/// </summary>
public class InterestService(AppDbContext db)
{
    public async Task<List<InterestDto>> GetTopAsync(Guid userId, int take = 5)
    {
        var now = DateTime.UtcNow;

        var events = await db.UserEvents
            .Where(e => e.UserId == userId && e.SkillId != null)
            .Select(e => new { SkillId = e.SkillId!.Value, e.Weight, e.CreatedAt })
            .ToListAsync();

        var top = events
            .GroupBy(e => e.SkillId)
            .Select(g => new
            {
                SkillId = g.Key,
                Score = g.Sum(e => e.Weight * Math.Pow(0.5, (now - e.CreatedAt).TotalDays / 7))
            })
            .OrderByDescending(x => x.Score)
            .Take(take)
            .ToList();

        var ids = top.Select(t => t.SkillId).ToList();
        var skills = await db.Skills.AsNoTracking().Where(s => ids.Contains(s.Id)).ToDictionaryAsync(s => s.Id);

        return top.Select(t => new InterestDto(skills[t.SkillId].ToDto(), Math.Round(t.Score, 2))).ToList();
    }
}
