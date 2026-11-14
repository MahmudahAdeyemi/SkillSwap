// =====================================================================
//  SkillSwap - Full API Contracts (.NET 8, C# 12)
//  Style: REST + JSON, JWT bearer auth, RFC 7807 ProblemDetails errors,
//         SignalR for real-time chat.
//  Conventions:
//    - All routes are prefixed with /api and require auth unless marked [public]
//    - All timestamps are UTC (ISO 8601)
//    - Lists that can grow are paged: ?page=1&pageSize=20 (max 50)
//    - Enums serialize as strings (JsonStringEnumConverter)
// =====================================================================
//
//  ENDPOINT MAP
//  ---------------------------------------------------------------------
//  AUTH
//    POST   /api/auth/register                 [public]  RegisterRequest -> AuthResponse (201)
//    POST   /api/auth/login                    [public]  LoginRequest    -> AuthResponse
//    GET    /api/auth/me                                 -> UserDto
//
//  USERS / PROFILE
//    GET    /api/users/{id}                              -> PublicProfileDto
//    PUT    /api/users/me                                UpdateProfileRequest -> UserDto
//    POST   /api/users/me/onboarding                     OnboardingRequest -> 204 (cold-start interests)
//    GET    /api/users/me/interests                      -> List<InterestDto>
//    GET    /api/users/{id}/reviews?page=                -> PagedResult<ReviewDto>
//
//  SKILLS CATALOG
//    GET    /api/skills?query=&category=&page=  [public] -> PagedResult<SkillDto>
//    POST   /api/skills                                  CreateSkillRequest -> SkillDto (201)
//    GET    /api/skills/categories              [public] -> List<string>
//
//  MY SKILLS
//    GET    /api/users/me/skills                         -> MySkillsDto
//    POST   /api/users/me/skills                         AddUserSkillRequest -> UserSkillDto (201)
//    PUT    /api/users/me/skills/{id}                    UpdateUserSkillRequest -> UserSkillDto
//    DELETE /api/users/me/skills/{id}                    -> 204
//
//  MATCHING
//    GET    /api/matches?kind=mutual|partial|chain&page=
//                                                        -> PagedResult<MatchDto>  (chain -> List<ChainMatchDto>)
//
//  DISCOVERY (home page)
//    GET    /api/discover                                -> DiscoverDto
//    POST   /api/events                                  TrackEventRequest -> 202 (fire-and-forget)
//
//  SWAPS
//    POST   /api/swaps                                   CreateSwapRequest -> SwapDto (201)
//    GET    /api/swaps?status=&page=                     -> PagedResult<SwapDto>
//    GET    /api/swaps/{id}                              -> SwapDto
//    POST   /api/swaps/{id}/accept                       -> SwapDto   (receiver only, status Proposed)
//    POST   /api/swaps/{id}/decline                      -> SwapDto   (receiver only, status Proposed)
//    POST   /api/swaps/{id}/cancel                       -> SwapDto   (either party, Proposed/Active)
//    POST   /api/swaps/{id}/complete                     -> SwapDto   (each side calls once)
//
//  CHAT
//    GET    /api/swaps/{id}/messages?before=&limit=      -> List<MessageDto>
//    POST   /api/swaps/{id}/messages                     SendMessageRequest -> MessageDto (201)
//    WS     /hubs/chat                                   SignalR (see IChatHub / IChatClient)
//
//  SESSIONS (scheduled lessons)
//    POST   /api/swaps/{id}/sessions                     CreateSessionRequest -> SessionDto (201)
//    GET    /api/swaps/{id}/sessions                     -> List<SessionDto>
//    PATCH  /api/sessions/{id}                           UpdateSessionRequest -> SessionDto
//
//  REVIEWS
//    POST   /api/swaps/{id}/reviews                      CreateReviewRequest -> ReviewDto (201)
//
//  NOTIFICATIONS (optional)
//    GET    /api/notifications?unreadOnly=               -> List<NotificationDto>
//    POST   /api/notifications/{id}/read                 -> 204
//
//  IMPACT
//    GET    /api/stats/impact                   [public] -> ImpactStatsDto
//
// =====================================================================

using System.ComponentModel.DataAnnotations;

namespace SkillSwap.Contracts;

// ---------------------------------------------------------------------
// ENUMS
// ---------------------------------------------------------------------
public enum SkillType     { Offering, Wanted }
public enum SkillLevel    { Beginner, Intermediate, Advanced }
public enum MatchKind     { Mutual, Partial, Chain }
public enum SwapStatus    { Proposed, Active, Completed, Declined, Cancelled }
public enum SessionStatus { Scheduled, Done, Cancelled }
public enum EventType     { Search, ViewProfile, AddWanted, ProposeSwap, CompleteSwap }
public enum ShelfType     { People, Skills }
public enum NotificationType
{
    SwapProposed, SwapAccepted, SwapDeclined, SwapCompleted,
    NewMessage, SessionScheduled, SessionChanged, ReviewReceived
}

// ---------------------------------------------------------------------
// COMMON
// ---------------------------------------------------------------------
public record PagedResult<T>(List<T> Items, int Page, int PageSize, int Total);

// Errors: use ProblemDetails (RFC 7807).
//   400 -> ValidationProblemDetails (field errors)
//   401 -> not authenticated        403 -> not allowed (e.g. not a swap participant)
//   404 -> not found                409 -> conflict (duplicate skill, invalid state transition)
//   422 -> business rule broken (e.g. swap with yourself)

// ---------------------------------------------------------------------
// AUTH
// ---------------------------------------------------------------------
public record RegisterRequest(
    [property: Required, StringLength(80, MinimumLength = 2)] string Name,
    [property: Required, EmailAddress] string Email,
    [property: Required, StringLength(100, MinimumLength = 8)] string Password);

public record LoginRequest(
    [property: Required, EmailAddress] string Email,
    [property: Required] string Password);

public record AuthResponse(string Token, DateTime ExpiresAt, UserDto User);

// ---------------------------------------------------------------------
// USERS
// ---------------------------------------------------------------------
public record UserDto(
    Guid Id,
    string Name,
    string? Bio,
    string? Location,
    string? TimeZone,
    string? AvatarUrl,
    double AverageRating,       // 0 when no reviews
    int ReviewCount,
    int CompletedSwaps,
    DateTime JoinedAt);

public record UpdateProfileRequest(
    [property: Required, StringLength(80, MinimumLength = 2)] string Name,
    [property: StringLength(500)] string? Bio,
    [property: StringLength(100)] string? Location,
    [property: StringLength(60)]  string? TimeZone,
    [property: Url] string? AvatarUrl);

public record PublicProfileDto(
    UserDto User,
    List<UserSkillDto> Offering,
    List<UserSkillDto> Wanted);

// Cold start: user picks interests when they first sign up.
public record OnboardingRequest(
    [property: MinLength(1), MaxLength(10)] List<int> InterestSkillIds,
    List<int>? OfferingSkillIds);

public record InterestDto(SkillDto Skill, double Score);

// ---------------------------------------------------------------------
// SKILLS
// ---------------------------------------------------------------------
public record SkillDto(int Id, string Name, string Category);

public record CreateSkillRequest(
    [property: Required, StringLength(60, MinimumLength = 2)] string Name,
    [property: Required, StringLength(40)] string Category);

public record UserSkillDto(
    int Id,                     // UserSkill row id (used for delete/update)
    SkillDto Skill,
    SkillType Type,
    SkillLevel Level,
    string? Notes);

public record AddUserSkillRequest(
    int SkillId,
    SkillType Type,
    SkillLevel Level,
    [property: StringLength(300)] string? Notes);

public record UpdateUserSkillRequest(
    SkillLevel Level,
    [property: StringLength(300)] string? Notes);

public record MySkillsDto(List<UserSkillDto> Offering, List<UserSkillDto> Wanted);

// ---------------------------------------------------------------------
// MATCHING
// ---------------------------------------------------------------------
public record MatchDto(
    UserDto User,
    MatchKind Kind,
    int Score,                              // higher = better
    List<SkillDto> TheyCanTeachMe,
    List<SkillDto> ICanTeachThem);

// GET /api/matches?kind=chain  (A teaches B, B teaches C, C teaches A)
public record ChainMatchDto(List<ChainLinkDto> Links, int Score);
public record ChainLinkDto(UserDto Teacher, UserDto Learner, SkillDto Skill);

// ---------------------------------------------------------------------
// DISCOVERY (Netflix-style home page)
// ---------------------------------------------------------------------
// POST /api/events - client fires this on search, profile view, etc.
// Server assigns the weight (client never sends it).
//   Search=1, ViewProfile=2, AddWanted=5, ProposeSwap=6, CompleteSwap=4
public record TrackEventRequest(
    EventType Type,
    int? SkillId,
    Guid? TargetUserId,
    [property: StringLength(100)] string? Query);

public record DiscoverDto(List<ShelfDto> Shelves, List<InterestDto> TopInterests);

public record ShelfDto(
    string Id,                  // "because-you-searched", "mutual", "want-what-you-teach",
                                // "related-skills", "trending", "new-members"
    string Title,               // "Because you searched Graphic Design"
    string? Reason,             // optional subtitle
    ShelfType Type,
    List<DiscoverItemDto> Items);

public record DiscoverItemDto(
    UserDto? User,              // set when shelf Type = People
    SkillDto? Skill,            // set when shelf Type = Skills (or the relevant skill for a person card)
    string? Badge,              // "Mutual match", "Trending", "New"
    string? Reason,             // e.g. "Teaches Figma, pairs with your interest in Branding"
    int? Score);

// ---------------------------------------------------------------------
// SWAPS
// ---------------------------------------------------------------------
public record CreateSwapRequest(
    Guid PartnerId,
    int SkillIWillTeachId,                  // must be in my Offering list
    int SkillIWillLearnId,                  // must be in partner's Offering list
    [property: Range(1, 40)] int HoursEach, // equal hours both ways = fairness
    [property: StringLength(500)] string? Message);

public record SwapDto(
    Guid Id,
    UserDto Proposer,
    UserDto Receiver,
    SkillDto ProposerTeaches,
    SkillDto ReceiverTeaches,
    int HoursEach,
    string? Message,
    SwapStatus Status,
    bool ProposerCompleted,
    bool ReceiverCompleted,
    int UnreadMessages,
    DateTime CreatedAt,
    DateTime? AcceptedAt,
    DateTime? CompletedAt);

// ---------------------------------------------------------------------
// CHAT
// ---------------------------------------------------------------------
public record SendMessageRequest([property: Required, StringLength(2000, MinimumLength = 1)] string Text);

public record MessageDto(long Id, Guid SwapId, Guid SenderId, string Text, DateTime SentAt);

// SignalR hub: /hubs/chat  (JWT passed via ?access_token= on the WebSocket connection)
public interface IChatHub
{
    Task JoinSwap(Guid swapId);                       // server verifies the caller is a participant
    Task LeaveSwap(Guid swapId);
    Task SendMessage(Guid swapId, string text);
    Task Typing(Guid swapId);
}

public interface IChatClient
{
    Task ReceiveMessage(MessageDto message);
    Task UserTyping(Guid swapId, Guid userId);
    Task SwapStatusChanged(Guid swapId, SwapStatus status);
}

// ---------------------------------------------------------------------
// SESSIONS
// ---------------------------------------------------------------------
public record CreateSessionRequest(
    Guid TeacherId,                         // must be one of the two swap participants
    int SkillId,                            // must be one of the two swap skills
    DateTime ScheduledAt,
    [property: Range(15, 240)] int DurationMinutes,
    [property: StringLength(300)] string? MeetingLink);

public record UpdateSessionRequest(
    DateTime? ScheduledAt,
    SessionStatus? Status,
    [property: StringLength(500)] string? Notes,
    [property: StringLength(300)] string? MeetingLink);

public record SessionDto(
    Guid Id,
    Guid SwapId,
    UserDto Teacher,
    SkillDto Skill,
    DateTime ScheduledAt,
    int DurationMinutes,
    SessionStatus Status,
    string? MeetingLink,
    string? Notes);

// ---------------------------------------------------------------------
// REVIEWS
// ---------------------------------------------------------------------
public record CreateReviewRequest(
    [property: Range(1, 5)] int Rating,
    [property: StringLength(1000)] string? Comment);

public record ReviewDto(
    Guid Id,
    Guid SwapId,
    UserDto Reviewer,
    int Rating,
    string? Comment,
    DateTime CreatedAt);

// ---------------------------------------------------------------------
// NOTIFICATIONS (optional)
// ---------------------------------------------------------------------
public record NotificationDto(
    long Id,
    NotificationType Type,
    string Text,
    Guid? SwapId,
    bool IsRead,
    DateTime CreatedAt);

// ---------------------------------------------------------------------
// IMPACT DASHBOARD
// ---------------------------------------------------------------------
public record ImpactStatsDto(
    int TotalUsers,
    int ActiveSwaps,
    int CompletedSwaps,
    int HoursExchanged,
    decimal EstimatedMoneySaved,            // hours * assumed average lesson price
    string Currency,                        // e.g. "USD" or "NGN"
    List<SkillCountDto> TopSkills);

public record SkillCountDto(string Skill, int Count);

// =====================================================================
//  BUSINESS RULES TO ENFORCE SERVER-SIDE
// =====================================================================
//  - Cannot swap with yourself.
//  - SkillIWillTeach must be in my Offering list; SkillIWillLearn must be in
//    the partner's Offering list.
//  - Only one open (Proposed/Active) swap per pair + skill combination.
//  - Only the receiver can accept/decline, and only while Proposed.
//  - Swap becomes Completed only when BOTH sides call /complete.
//  - Only swap participants can read chat, sessions, or details of a swap.
//  - Chat and sessions are only allowed while the swap is Active.
//  - Reviews: only on Completed swaps, once per person per swap, 1-5 stars.
//  - Same user cannot add the same skill twice with the same Type (409).
//  - /api/events must be cheap: validate, insert, return 202. Never block UI.
//  - Discover: dedupe people across shelves, hide users with an open swap,
//    cache per user for ~60s.
// =====================================================================
