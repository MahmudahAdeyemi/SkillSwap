using Microsoft.AspNetCore.Identity;

namespace SkillSwap.Api.Domain;

public class AppUser : IdentityUser<Guid>
{
    public string Name { get; set; } = "";
    public string? Bio { get; set; }
    public string? Location { get; set; }
    public string? TimeZone { get; set; }
    public string? AvatarUrl { get; set; }
    public DateTime JoinedAt { get; set; } = DateTime.UtcNow;

    public List<UserSkill> Skills { get; set; } = new();
}

public class Skill
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Category { get; set; } = "";
}

/// <summary>A skill a user offers (can teach) or wants (wants to learn).</summary>
public class UserSkill
{
    public int Id { get; set; }
    public Guid UserId { get; set; }
    public AppUser User { get; set; } = null!;
    public int SkillId { get; set; }
    public Skill Skill { get; set; } = null!;
    public SkillType Type { get; set; }
    public SkillLevel Level { get; set; }
    public string? Notes { get; set; }
}

public class Swap
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid ProposerId { get; set; }
    public AppUser Proposer { get; set; } = null!;
    public Guid ReceiverId { get; set; }
    public AppUser Receiver { get; set; } = null!;

    public int ProposerTeachesId { get; set; }
    public Skill ProposerTeaches { get; set; } = null!;
    public int ReceiverTeachesId { get; set; }
    public Skill ReceiverTeaches { get; set; } = null!;

    public int HoursEach { get; set; }
    public string? Message { get; set; }
    public SwapStatus Status { get; set; } = SwapStatus.Proposed;
    public bool ProposerCompleted { get; set; }
    public bool ReceiverCompleted { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? AcceptedAt { get; set; }
    public DateTime? CompletedAt { get; set; }

    public List<Message> Messages { get; set; } = new();
    public List<LessonSession> Sessions { get; set; } = new();
}

public class Message
{
    public long Id { get; set; }
    public Guid SwapId { get; set; }
    public Swap Swap { get; set; } = null!;
    public Guid SenderId { get; set; }
    public AppUser Sender { get; set; } = null!;
    public string Text { get; set; } = "";
    public DateTime SentAt { get; set; } = DateTime.UtcNow;
}

public class LessonSession
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SwapId { get; set; }
    public Swap Swap { get; set; } = null!;
    public Guid TeacherId { get; set; }
    public AppUser Teacher { get; set; } = null!;
    public int SkillId { get; set; }
    public Skill Skill { get; set; } = null!;
    public DateTime ScheduledAt { get; set; }
    public int DurationMinutes { get; set; }
    public SessionStatus Status { get; set; } = SessionStatus.Scheduled;
    public string? MeetingLink { get; set; }
    public string? Notes { get; set; }
}

public class Review
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SwapId { get; set; }
    public Swap Swap { get; set; } = null!;
    public Guid ReviewerId { get; set; }
    public AppUser Reviewer { get; set; } = null!;
    public Guid RevieweeId { get; set; }
    public AppUser Reviewee { get; set; } = null!;
    public int Rating { get; set; }
    public string? Comment { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>Behavior log that powers the discover page.</summary>
public class UserEvent
{
    public long Id { get; set; }
    public Guid UserId { get; set; }
    public EventType Type { get; set; }
    public int? SkillId { get; set; }
    public Guid? TargetUserId { get; set; }
    public string? Query { get; set; }
    public int Weight { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public static class EventWeights
{
    public static int For(EventType type) => type switch
    {
        EventType.Search => 1,
        EventType.ViewProfile => 2,
        EventType.AddWanted => 5,
        EventType.ProposeSwap => 6,
        EventType.CompleteSwap => 4,
        _ => 1
    };
}
