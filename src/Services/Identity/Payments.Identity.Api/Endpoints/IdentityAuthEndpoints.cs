using Payments.Identity.Application.Contracts;
using Payments.Identity.Application.Security;

namespace Payments.Identity.Api.Endpoints;

public static class IdentityAuthEndpoints
{
    public static IEndpointRouteBuilder MapIdentityAuthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/auth").WithTags("Authentication");
        group.MapPost("/register", async (RegisterUserRequest request, IIdentityService service, HttpContext context, CancellationToken cancellationToken) =>
        {
            var response = await service.RegisterAsync(request, GetIp(context), GetUserAgent(context), cancellationToken).ConfigureAwait(false);
            return Results.Created($"/api/v1/users/{response.UserId}", response);
        }).RequireRateLimiting("auth-sensitive");

        group.MapPost("/login", async (LoginRequest request, IIdentityService service, HttpContext context, CancellationToken cancellationToken) =>
        {
            var response = await service.LoginAsync(request, GetIp(context), GetUserAgent(context), cancellationToken).ConfigureAwait(false);
            return Results.Ok(response);
        }).RequireRateLimiting("auth-sensitive");

        group.MapPost("/refresh", async (RefreshTokenRequest request, IIdentityService service, HttpContext context, CancellationToken cancellationToken) =>
        {
            var response = await service.RefreshAsync(request, GetIp(context), GetUserAgent(context), cancellationToken).ConfigureAwait(false);
            return Results.Ok(response);
        }).RequireRateLimiting("auth-sensitive");

        group.MapPost("/logout", async (LogoutRequest request, IIdentityService service, HttpContext context, CancellationToken cancellationToken) =>
        {
            await service.LogoutAsync(request, GetIp(context), GetUserAgent(context), cancellationToken).ConfigureAwait(false);
            return Results.NoContent();
        }).RequireAuthorization().RequireRateLimiting("auth-sensitive");

        return endpoints;
    }

    private static string? GetIp(HttpContext context) => context.Connection.RemoteIpAddress?.ToString();

    private static string? GetUserAgent(HttpContext context) => context.Request.Headers.UserAgent.ToString();
}
