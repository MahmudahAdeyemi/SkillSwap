using System.ComponentModel.DataAnnotations;

namespace SkillSwap.Api.Services;

/// <summary>
/// Minimal APIs don't run DataAnnotations by default. Add .Validate&lt;T&gt;() to an endpoint
/// to check the request body and return a 400 ValidationProblemDetails on failure.
/// </summary>
public class ValidationFilter<T> : IEndpointFilter where T : class
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext ctx, EndpointFilterDelegate next)
    {
        var arg = ctx.Arguments.OfType<T>().FirstOrDefault();
        if (arg is null)
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["body"] = new[] { "Request body is required." }
            });

        var results = new List<ValidationResult>();
        if (!Validator.TryValidateObject(arg, new ValidationContext(arg), results, validateAllProperties: true))
        {
            var errors = results
                .SelectMany(r => (r.MemberNames.Any() ? r.MemberNames : new[] { "" })
                    .Select(m => (Member: m, Message: r.ErrorMessage ?? "Invalid value.")))
                .GroupBy(x => x.Member)
                .ToDictionary(g => g.Key, g => g.Select(x => x.Message).ToArray());
            return Results.ValidationProblem(errors);
        }

        return await next(ctx);
    }
}

public static class ValidationExtensions
{
    public static RouteHandlerBuilder Validate<T>(this RouteHandlerBuilder b) where T : class =>
        b.AddEndpointFilter<ValidationFilter<T>>();
}
