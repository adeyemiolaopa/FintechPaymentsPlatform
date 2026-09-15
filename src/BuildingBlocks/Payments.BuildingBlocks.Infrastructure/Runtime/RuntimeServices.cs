using System.Diagnostics;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Payments.BuildingBlocks.Application.Abstractions;

namespace Payments.BuildingBlocks.Infrastructure.Runtime;

public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}

public sealed class CurrentUser : ICurrentUser
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CurrentUser(IHttpContextAccessor httpContextAccessor) => _httpContextAccessor = httpContextAccessor;

    public string? UserId => FindFirst("sub") ?? FindFirst(ClaimTypes.NameIdentifier);

    public string? CustomerId => FindFirst("customer_id");

    public IReadOnlyCollection<string> Roles => FindMany(ClaimTypes.Role, "role");

    public IReadOnlyCollection<string> Permissions => FindMany("permission", "permissions");

    public bool IsAuthenticated => _httpContextAccessor.HttpContext?.User.Identity?.IsAuthenticated == true;

    public bool HasPermission(string permission) => Permissions.Contains(permission, StringComparer.OrdinalIgnoreCase);

    private string? FindFirst(string claimType) => _httpContextAccessor.HttpContext?.User.FindFirstValue(claimType);

    private IReadOnlyCollection<string> FindMany(params string[] claimTypes)
    {
        var user = _httpContextAccessor.HttpContext?.User;
        if (user is null)
        {
            return Array.Empty<string>();
        }

        return claimTypes
            .SelectMany(type => user.FindAll(type))
            .SelectMany(claim => claim.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}

public sealed class RequestContext : IRequestContext
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public RequestContext(IHttpContextAccessor httpContextAccessor) => _httpContextAccessor = httpContextAccessor;

    public string CorrelationId => _httpContextAccessor.HttpContext?.Items["CorrelationId"]?.ToString() ?? Activity.Current?.TraceId.ToString() ?? Guid.Empty.ToString("D");

    public string? CausationId => _httpContextAccessor.HttpContext?.Request.Headers["X-Causation-Id"].ToString();

    public string TraceId => Activity.Current?.TraceId.ToString() ?? _httpContextAccessor.HttpContext?.TraceIdentifier ?? string.Empty;
}

public static class RuntimeServiceCollectionExtensions
{
    public static IServiceCollection AddRuntimeContext(this IServiceCollection services)
    {
        services.AddHttpContextAccessor();
        services.AddSingleton<IClock, SystemClock>();
        services.AddScoped<ICurrentUser, CurrentUser>();
        services.AddScoped<IRequestContext, RequestContext>();
        return services;
    }
}
