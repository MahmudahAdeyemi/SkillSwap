namespace SkillSwap.Api.Services;

/// <summary>Maps Swap entities (with ProposerTeaches/ReceiverTeaches loaded) to SwapDto.</summary>
public class SwapMapper(UserMapper users)
{
    public async Task<List<SwapDto>> ToDtosAsync(IReadOnlyCollection<Swap> swaps)
    {
        if (swaps.Count == 0) return new();

        var userMap = await users.GetManyAsync(swaps.SelectMany(s => new[] { s.ProposerId, s.ReceiverId }));

        // UnreadMessages is 0 for now. Read tracking arrives with the chat phase.
        return swaps.Select(s => new SwapDto(
            s.Id,
            userMap[s.ProposerId],
            userMap[s.ReceiverId],
            s.ProposerTeaches.ToDto(),
            s.ReceiverTeaches.ToDto(),
            s.HoursEach,
            s.Message,
            s.Status,
            s.ProposerCompleted,
            s.ReceiverCompleted,
            0,
            s.CreatedAt,
            s.AcceptedAt,
            s.CompletedAt)).ToList();
    }

    public async Task<SwapDto> ToDtoAsync(Swap swap) => (await ToDtosAsync(new[] { swap }))[0];
}
