using Microsoft.AspNetCore.Identity;

namespace SkillSwap.Api.Endpoints;

public static class AuthEndpoints
{
    public static void MapAuth(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/auth").WithTags("Auth");

        g.MapPost("/register", async (RegisterRequest req, UserManager<AppUser> users,
            TokenService tokens, UserMapper mapper) =>
        {
            var user = new AppUser
            {
                Id = Guid.NewGuid(),
                UserName = req.Email.Trim(),
                Email = req.Email.Trim(),
                Name = req.Name.Trim(),
                JoinedAt = DateTime.UtcNow
            };

            var result = await users.CreateAsync(user, req.Password);
            if (!result.Succeeded)
            {
                var errors = result.Errors
                    .GroupBy(e => e.Code)
                    .ToDictionary(x => x.Key, x => x.Select(e => e.Description).ToArray());
                return Results.ValidationProblem(errors);
            }

            var dto = (await mapper.GetAsync(user.Id))!;
            var (token, expires) = tokens.Create(user);
            return Results.Created("/api/auth/me", new AuthResponse(token, expires, dto));
        })
        .AllowAnonymous()
        .Validate<RegisterRequest>();

        g.MapPost("/login", async (LoginRequest req, UserManager<AppUser> users,
            TokenService tokens, UserMapper mapper) =>
        {
            var user = await users.FindByEmailAsync(req.Email.Trim());
            if (user is null || !await users.CheckPasswordAsync(user, req.Password))
                return Results.Problem(statusCode: 401, title: "Invalid email or password.");

            var dto = (await mapper.GetAsync(user.Id))!;
            var (token, expires) = tokens.Create(user);
            return Results.Ok(new AuthResponse(token, expires, dto));
        })
        .AllowAnonymous()
        .Validate<LoginRequest>();

        g.MapGet("/me", async (System.Security.Claims.ClaimsPrincipal p, UserMapper mapper) =>
        {
            var dto = await mapper.GetAsync(p.GetUserId());
            return dto is null ? Results.NotFound() : Results.Ok(dto);
        })
        .RequireAuthorization();
    }
}
