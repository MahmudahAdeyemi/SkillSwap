using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace SkillSwap.Api.Services;

public static class Mapping
{
    public static SkillDto ToDto(this Skill s) => new(s.Id, s.Name, s.Category);

    public static UserSkillDto ToDto(this UserSkill us) =>
        new(us.Id, us.Skill.ToDto(), us.Type, us.Level, us.Notes);

    /// <summary>Reads the user id from the JWT "sub" claim.</summary>
    public static Guid GetUserId(this ClaimsPrincipal p) =>
        Guid.Parse(p.FindFirstValue(JwtRegisteredClaimNames.Sub)
                   ?? throw new UnauthorizedAccessException("Missing user id claim."));
}
