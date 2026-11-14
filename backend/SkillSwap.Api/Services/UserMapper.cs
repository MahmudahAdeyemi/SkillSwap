namespace SkillSwap.Api.Services;

/// <summary>
/// Builds UserDto objects (including rating + completed swap counts) in batches,
/// so match lists and discover shelves don't trigger N+1 queries.
/// </summary>
public class UserMapper(AppDbContext db)
{
    public async Task<Dictionary<Guid, UserDto>> GetManyAsync(IEnumerable<Guid> ids)
    {
        var list = ids.Distinct().ToList();
        if (list.Count == 0) return new();

        var users = await db.Users.AsNoTracking().Where(u => list.Contains(u.Id)).ToListAsync();

        var ratings = (await db.Reviews
                .Where(r => list.Contains(r.RevieweeId))
                .GroupBy(r => r.RevieweeId)
                .Select(g => new { Id = g.Key, Avg = g.Average(x => x.Rating), Count = g.Count() })
                .ToListAsync())
            .ToDictionary(x => x.Id);

        var swaps = await db.Swaps
            .Where(s => s.Status == SwapStatus.Completed &&
                        (list.Contains(s.ProposerId) || list.Contains(s.ReceiverId)))
            .Select(s => new { s.ProposerId, s.ReceiverId })
            .ToListAsync();

        var completed = new Dictionary<Guid, int>();
        foreach (var s in swaps)
            foreach (var id in new[] { s.ProposerId, s.ReceiverId })
                if (list.Contains(id))
                    completed[id] = completed.GetValueOrDefault(id) + 1;

        return users.ToDictionary(u => u.Id, u =>
        {
            ratings.TryGetValue(u.Id, out var r);
            return new UserDto(
                u.Id, u.Name, u.Bio, u.Location, u.TimeZone, u.AvatarUrl,
                r is null ? 0 : Math.Round(r.Avg, 1),
                r?.Count ?? 0,
                completed.GetValueOrDefault(u.Id),
                u.JoinedAt);
        });
    }

    public async Task<UserDto?> GetAsync(Guid id) =>
        (await GetManyAsync(new[] { id })).GetValueOrDefault(id);
}
