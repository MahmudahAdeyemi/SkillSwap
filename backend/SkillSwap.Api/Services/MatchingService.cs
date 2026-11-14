namespace SkillSwap.Api.Services;

/// <summary>
/// The core of SkillSwap: finds people whose skills fit yours.
///   Mutual  = they teach something I want AND I teach something they want.
///   Partial = only one direction fits.
///   Chain   = A teaches B, B teaches C, C teaches A (when no direct pair exists).
/// People you already have an open (Proposed/Active) swap with are hidden.
/// </summary>
public class MatchingService(AppDbContext db, UserMapper users)
{
    public async Task<List<MatchDto>> FindMatchesAsync(Guid me, MatchKind kind)
    {
        if (kind == MatchKind.Chain)
            throw new ArgumentException("Use FindChainsAsync for chain matches.", nameof(kind));

        var mine = await db.UserSkills.AsNoTracking()
            .Where(x => x.UserId == me)
            .Select(x => new { x.SkillId, x.Type })
            .ToListAsync();

        var offerList = mine.Where(x => x.Type == SkillType.Offering).Select(x => x.SkillId).ToList();
        var wantList = mine.Where(x => x.Type == SkillType.Wanted).Select(x => x.SkillId).ToList();
        if (offerList.Count == 0 && wantList.Count == 0) return new();

        // Every row from other people that touches one of my skills.
        var rows = await db.UserSkills.AsNoTracking()
            .Where(x => x.UserId != me &&
                ((x.Type == SkillType.Offering && wantList.Contains(x.SkillId)) ||
                 (x.Type == SkillType.Wanted && offerList.Contains(x.SkillId))))
            .Select(x => new { x.UserId, x.SkillId, x.Type })
            .ToListAsync();

        var busy = await UsersWithOpenSwapAsync(me);

        var grouped = rows
            .Where(r => !busy.Contains(r.UserId))
            .GroupBy(r => r.UserId)
            .Select(g => new
            {
                UserId = g.Key,
                TheyTeach = g.Where(r => r.Type == SkillType.Offering).Select(r => r.SkillId).Distinct().ToList(),
                ITeach = g.Where(r => r.Type == SkillType.Wanted).Select(r => r.SkillId).Distinct().ToList()
            })
            .Where(x => kind == MatchKind.Mutual
                ? x.TheyTeach.Count > 0 && x.ITeach.Count > 0
                : (x.TheyTeach.Count > 0) != (x.ITeach.Count > 0))
            .ToList();

        if (grouped.Count == 0) return new();

        var userMap = await users.GetManyAsync(grouped.Select(x => x.UserId));
        var skillIds = grouped.SelectMany(x => x.TheyTeach.Concat(x.ITeach)).Distinct().ToList();
        var skillMap = (await db.Skills.AsNoTracking().Where(s => skillIds.Contains(s.Id)).ToListAsync())
            .ToDictionary(s => s.Id, s => s.ToDto());

        return grouped
            .Where(x => userMap.ContainsKey(x.UserId))
            .Select(x => new MatchDto(
                userMap[x.UserId],
                kind,
                x.TheyTeach.Count + x.ITeach.Count + (kind == MatchKind.Mutual ? 10 : 0),
                x.TheyTeach.Select(id => skillMap[id]).OrderBy(s => s.Name).ToList(),
                x.ITeach.Select(id => skillMap[id]).OrderBy(s => s.Name).ToList()))
            .OrderByDescending(m => m.Score)
            .ThenByDescending(m => m.User.AverageRating)
            .ThenBy(m => m.User.Name)
            .ToList();
    }

    /// <summary>
    /// Three-way swaps: me -> B -> C -> me.
    /// Skipped when B or C is already a direct (mutual) partner, since a direct swap is simpler.
    /// Loads all skill rows into memory, which is fine at hackathon scale.
    /// </summary>
    public async Task<List<ChainMatchDto>> FindChainsAsync(Guid me, int take = 20)
    {
        var all = await db.UserSkills.AsNoTracking()
            .Select(x => new { x.UserId, x.SkillId, x.Type })
            .ToListAsync();

        var offers = all.Where(x => x.Type == SkillType.Offering)
            .GroupBy(x => x.UserId).ToDictionary(g => g.Key, g => g.Select(x => x.SkillId).ToHashSet());
        var wants = all.Where(x => x.Type == SkillType.Wanted)
            .GroupBy(x => x.UserId).ToDictionary(g => g.Key, g => g.Select(x => x.SkillId).ToHashSet());

        if (!offers.TryGetValue(me, out var myOffers) || !wants.TryGetValue(me, out var myWants))
            return new();

        var busy = await UsersWithOpenSwapAsync(me);

        bool IsMutualWithMe(Guid other) =>
            offers.TryGetValue(other, out var o) && wants.TryGetValue(other, out var w) &&
            o.Overlaps(myWants) && w.Overlaps(myOffers);

        // B wants something I teach. C teaches something I want.
        var bs = wants.Where(kv => kv.Key != me && !busy.Contains(kv.Key) && kv.Value.Overlaps(myOffers) && !IsMutualWithMe(kv.Key))
            .Select(kv => kv.Key).ToList();
        var cs = offers.Where(kv => kv.Key != me && !busy.Contains(kv.Key) && kv.Value.Overlaps(myWants) && !IsMutualWithMe(kv.Key))
            .Select(kv => kv.Key).ToList();

        var found = new List<(Guid B, Guid C, int S1, int S2, int S3)>();
        foreach (var b in bs)
        {
            foreach (var c in cs)
            {
                if (b == c) continue;
                if (!offers.TryGetValue(b, out var bOffers) || !wants.TryGetValue(c, out var cWants)) continue;

                var s2 = bOffers.Where(cWants.Contains).OrderBy(x => x).ToList();   // B teaches C
                if (s2.Count == 0) continue;

                var s1 = myOffers.Where(wants[b].Contains).OrderBy(x => x).First();   // I teach B
                var s3 = offers[c].Where(myWants.Contains).OrderBy(x => x).First();   // C teaches me
                found.Add((b, c, s1, s2[0], s3));
                if (found.Count >= 50) break;
            }
            if (found.Count >= 50) break;
        }
        if (found.Count == 0) return new();

        var userIds = found.SelectMany(f => new[] { f.B, f.C }).Append(me).Distinct().ToList();
        var userMap = await users.GetManyAsync(userIds);
        var skillIds = found.SelectMany(f => new[] { f.S1, f.S2, f.S3 }).Distinct().ToList();
        var skillMap = (await db.Skills.AsNoTracking().Where(s => skillIds.Contains(s.Id)).ToListAsync())
            .ToDictionary(s => s.Id, s => s.ToDto());

        return found
            .Select(f => new ChainMatchDto(
                new List<ChainLinkDto>
                {
                    new(userMap[me], userMap[f.B], skillMap[f.S1]),
                    new(userMap[f.B], userMap[f.C], skillMap[f.S2]),
                    new(userMap[f.C], userMap[me], skillMap[f.S3])
                },
                Score: 5))
            .OrderByDescending(c => c.Links[1].Teacher.AverageRating + c.Links[2].Teacher.AverageRating)
            .Take(take)
            .ToList();
    }

    private async Task<HashSet<Guid>> UsersWithOpenSwapAsync(Guid me)
    {
        var swaps = await db.Swaps.AsNoTracking()
            .Where(s => (s.Status == SwapStatus.Proposed || s.Status == SwapStatus.Active) &&
                        (s.ProposerId == me || s.ReceiverId == me))
            .Select(s => new { s.ProposerId, s.ReceiverId })
            .ToListAsync();

        return swaps.Select(s => s.ProposerId == me ? s.ReceiverId : s.ProposerId).ToHashSet();
    }
}
