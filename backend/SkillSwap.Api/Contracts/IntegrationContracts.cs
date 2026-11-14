namespace SkillSwap.Contracts;

public record SkillDetailDto(
    SkillDto Skill,
    int TeachersCount,
    int LearnersCount,
    int WantedCount,
    List<UserDto> Teachers,
    List<UserDto> Learners,
    List<SkillDto> RelatedSkills,
    CommunityPostDto? Discussion);

public record SearchResultDto(
    string Query,
    List<SkillDto> Skills,
    List<MatchDto> People,
    List<CommunityPostDto> Posts);
