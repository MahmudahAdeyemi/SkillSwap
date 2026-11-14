using System.Security.Claims;

namespace SkillSwap.Api.Endpoints;

public static class DiscoverEndpoints
{
    public static void MapDiscover(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/discover", async (ClaimsPrincipal p, AppDbContext db,
            MatchingService matching, InterestService interests, UserMapper mapper) =>
        {
            var isAuthenticated = p.Identity?.IsAuthenticated == true;
            Guid? me = isAuthenticated ? p.GetUserId() : null;

            // 1. Top interests from the user's behavior/onboarding. Guests get global shelves.
            var userInterests = me is Guid interestUserId
                ? await interests.GetTopAsync(interestUserId, 8)
                : new List<InterestDto>();

            var seenPeople = new HashSet<Guid>();

            // 2. Mutual matches (best reciprocal trades) for signed-in users.
            var mutualItems = new List<DiscoverItemDto>();
            if (me is Guid matchUserId)
            {
                var mutual = await matching.FindMatchesAsync(matchUserId, MatchKind.Mutual);
                mutualItems = mutual.Take(6).Select(m =>
                {
                    seenPeople.Add(m.User.Id);
                    var reason = m.TheyCanTeachMe.Count > 0 && m.ICanTeachThem.Count > 0
                        ? $"Teaches {m.TheyCanTeachMe[0].Name}, wants {m.ICanTeachThem[0].Name}"
                        : "Great match for your skills";

                    return new DiscoverItemDto(
                        m.User,
                        m.TheyCanTeachMe.FirstOrDefault(),
                        $"{m.Score}% Match",
                        reason,
                        m.Score);
                }).ToList();
            }

            // 3. Trending skills: most-listed skills, with a catalog fallback for new users.
            var trending = await db.UserSkills.AsNoTracking()
                .GroupBy(x => x.SkillId)
                .Select(g => new { SkillId = g.Key, Count = g.Count() })
                .OrderByDescending(x => x.Count)
                .Take(6)
                .ToListAsync();

            var trendingIds = trending.Select(t => t.SkillId).ToList();
            var trendingSkills = await db.Skills.AsNoTracking()
                .Where(s => trendingIds.Contains(s.Id))
                .ToListAsync();

            if (trendingSkills.Count == 0)
                trendingSkills = await db.Skills.AsNoTracking().OrderBy(s => s.Name).Take(6).ToListAsync();

            var trendingItems = trendingSkills
                .Select((s, i) => new DiscoverItemDto(
                    null,
                    s.ToDto(),
                    i == 0 ? "Trending" : null,
                    $"Popular in {s.Category}",
                    90 - i))
                .ToList();

            // 4. Partial matches / mentors (one-way fit) for signed-in users.
            var mentorItems = new List<DiscoverItemDto>();
            if (me is Guid partialUserId)
            {
                var partial = await matching.FindMatchesAsync(partialUserId, MatchKind.Partial);
                mentorItems = partial
                    .Where(m => seenPeople.Add(m.User.Id))
                    .Take(6)
                    .Select(m => new DiscoverItemDto(
                        m.User,
                        m.TheyCanTeachMe.FirstOrDefault(),
                        "Mentor",
                        m.TheyCanTeachMe.Count > 0 ? $"Can teach {m.TheyCanTeachMe[0].Name}" : "Active learner",
                        m.Score))
                    .ToList();
            }

            // 5. Community mentors: anyone offering a skill, sorted by rating. This is the
            //    public discovery shelf guests and brand-new accounts can explore.
            var busySet = new HashSet<Guid>();
            if (me is Guid busyUserId)
            {
                var busy = await db.Swaps.AsNoTracking()
                    .Where(s => (s.Status == SwapStatus.Proposed || s.Status == SwapStatus.Active) &&
                                (s.ProposerId == busyUserId || s.ReceiverId == busyUserId))
                    .Select(s => s.ProposerId == busyUserId ? s.ReceiverId : s.ProposerId)
                    .ToListAsync();
                busySet = busy.ToHashSet();
            }

            var mentorIds = await db.UserSkills.AsNoTracking()
                .Where(x => x.Type == SkillType.Offering &&
                            (!me.HasValue || x.UserId != me.Value) &&
                            !busySet.Contains(x.UserId))
                .Select(x => new { x.UserId, x.SkillId })
                .Distinct()
                .ToListAsync();
            var mentorUserIds = mentorIds.Select(x => x.UserId).Distinct().ToList();
            var mentorUsers = await mapper.GetManyAsync(mentorUserIds);
            var mentorSkill = mentorIds
                .GroupBy(x => x.UserId)
                .ToDictionary(g => g.Key, g => g.First().SkillId);
            var mentorSkillDtos = await db.Skills.AsNoTracking()
                .Where(s => mentorSkill.Values.Contains(s.Id))
                .ToDictionaryAsync(s => s.Id, s => s.ToDto());

            var communityMentors = mentorUserIds
                .Where(id => seenPeople.Add(id) && mentorUsers.ContainsKey(id))
                .Select(id => new { Id = id, User = mentorUsers[id] })
                .OrderByDescending(x => x.User.AverageRating)
                .ThenByDescending(x => x.User.CompletedSwaps)
                .Take(6)
                .Select(x => new DiscoverItemDto(
                    x.User,
                    mentorSkillDtos.GetValueOrDefault(mentorSkill[x.Id]),
                    "Mentor",
                    x.User.Location is null ? "Open to exchanging skills" : $"Based in {x.User.Location}",
                    80))
                .ToList();

            // 6. New members: useful for cold-start and public discovery.
            var newMemberIds = await db.Users.AsNoTracking()
                .Where(u => (!me.HasValue || u.Id != me.Value) &&
                            !busySet.Contains(u.Id) &&
                            !seenPeople.Contains(u.Id))
                .OrderByDescending(u => u.JoinedAt)
                .Take(6)
                .Select(u => u.Id)
                .ToListAsync();
            var newUsers = await mapper.GetManyAsync(newMemberIds);
            var newMemberItems = newMemberIds
                .Where(newUsers.ContainsKey)
                .Select(id =>
                {
                    var u = newUsers[id];
                    return new DiscoverItemDto(u, null, "New", $"Joined {u.JoinedAt:MMM yyyy}", 60);
                })
                .ToList();

            var shelves = new List<ShelfDto>();
            if (mutualItems.Count > 0)
                shelves.Add(new("mutual", "Recommended Matches", "People who can teach what you want to learn", ShelfType.People, mutualItems));
            if (trendingItems.Count > 0)
                shelves.Add(new("trending", "Trending Skills", "Most active skills across the community", ShelfType.Skills, trendingItems));
            if (communityMentors.Count > 0)
                shelves.Add(new("mentors", "Explore Mentors", "Experienced peers ready to exchange", ShelfType.People, communityMentors));
            else if (mentorItems.Count > 0)
                shelves.Add(new("partial", "People You Can Learn From", "One-way fits that can become a swap", ShelfType.People, mentorItems));
            if (newMemberItems.Count > 0)
                shelves.Add(new("new-members", "New on SkillBridge", "Welcome the newest members of the community", ShelfType.People, newMemberItems));

            return Results.Ok(new DiscoverDto(shelves, userInterests));
        })
        .WithTags("Discovery")
        .AllowAnonymous();
    }
}
